using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record SubmitProductionDemand(Guid SubmissionId, RunLineInput Line, string? PlanVersion,
    Guid? ProductionRunId = null, string? NewRunName = null, bool CreateRun = false,
    IReadOnlyDictionary<Guid, int>? PlateOverrides = null);

public sealed class ProductionDemandService(IProductionDemandRepository demands, IProductionRunRepository runs,
    IPartRequirementRepository requirements, IPrintJobRepository jobs, IPartRepository parts,
    IAssemblyGraphQueryService graph, IAssemblyRepository assemblies, IPartFileStorage files,
    ProductionSnapshotFactory snapshots, IPrinterRepository printers, ProductionPlanner planner,
    ProductionDispatchRequestService dispatch, IProductionEventPublisher publisher)
{
    private ProductionDemandMaterializer Materializer() => new(requirements, jobs, parts, graph, printers, assemblies, files, snapshots, planner);

    public async Task<ProductionDemand> SubmitAsync(SubmitProductionDemand command, CancellationToken ct)
    {
        if (command.SubmissionId == Guid.Empty) throw new InvalidProductionDemandException("Submission ID is required.");
        await using var submissionLock = await demands.LockAsync(command.SubmissionId, ct, command.Line.TargetKind == RunLineTargetKind.Assembly);
        var requestJson = System.Text.Json.JsonSerializer.Serialize(command with
        {
            PlateOverrides = command.PlateOverrides?.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key, pair => pair.Value)
        });
        var existing = await demands.FindSubmissionAsync(command.SubmissionId, ct);
        if (existing is not null)
        {
            if (existing.SubmissionRequestJson != requestJson)
                throw new InvalidProductionDemandException("This submission ID was already used for different work.");
            return existing;
        }
        var input = command.Line;
        if (input.Quantity is null && (command.ProductionRunId is not null || command.CreateRun))
            throw new InvalidProductionDemandException("Create continuous backfill in Queue instead of a new Run.");
        if (command.ProductionRunId is not null && command.CreateRun)
            throw new InvalidProductionDemandException("Choose an existing Run or a new Run.");
        ProductionPlan? plan = null;
        if (input.Quantity is { } quantity)
        {
            plan = await planner.PlanAsync(new(input.TargetKind, input.TargetId, quantity,
                input.QuantityBasis ?? (input.TargetKind == RunLineTargetKind.Part ? QuantityBasis.Plates : QuantityBasis.Sets), command.PlateOverrides), ct);
            if (plan.Version != command.PlanVersion) throw new ProductionPlanConflictException();
            if (plan.Plates < 1) throw new InvalidProductionDemandException("Queue at least one plate.");
            if ((command.ProductionRunId is not null || command.CreateRun) && plan.Parts.Any(p => p.SurplusUnits < 0))
                throw new InvalidProductionDemandException("Run plate counts must satisfy the requested target.");
        }
        var spec = input.ToSpec() with { QuantityBasis = input.QuantityBasis ?? (input.TargetKind == RunLineTargetKind.Part ? QuantityBasis.Plates : QuantityBasis.Sets) };
        ProductionRun? run = null;
        ProductionDemand demand;
        if (command.ProductionRunId is { } runId)
        {
            run = await runs.GetByIdAsync(runId, ct) ?? throw new ProductionRunNotFoundException(runId);
            demand = run.AddLines([spec])[0];
            runs.TrackNewLines([demand]);
        }
        else if (command.CreateRun)
        {
            run = ProductionRun.Create(command.NewRunName, null, null, [spec]);
            demand = run.Lines[0];
            await runs.AddAsync(run, ct);
        }
        else
        {
            demand = ProductionDemand.Create(null, spec);
            demands.Add(demand);
        }
        demand.SetSubmissionId(command.SubmissionId, requestJson);
        var materializer = Materializer();
        var exploded = await materializer.ExplodeAsync([demand], ct);
        await materializer.StageAsync([demand], exploded, ct, plan?.Parts.ToDictionary(p => p.PartId, p => p.Plates));
        if (plan is not null) await demands.ValidateSourcesAsync(plan, ct);
        // Re-plan immediately before the atomic save; the repository also locks source Parts before saving.
        if (plan is not null)
        {
            var fresh = await planner.PlanAsync(new(input.TargetKind, input.TargetId, input.Quantity!.Value, spec.QuantityBasis!.Value, command.PlateOverrides), ct);
            if (fresh.Version != plan.Version) throw new ProductionPlanConflictException();
        }
        await demands.SaveAsync(ct);
        if (run is not null) await publisher.ProductionRunStatusChangedAsync(run.Id, run.Status, ct);
        await publisher.DemandChangedAsync(demand.Id, ct);
        await dispatch.RequestIdlePrintersAsync(ct);
        return demand;
    }

    public async Task<ProductionDemand> ChangeBackfillAsync(Guid id, string action, int? printerCap, Guid? pinnedPrinterId, CancellationToken ct)
    {
        await using var demandLock = await demands.LockAsync(id, ct, action == "Refresh");
        var demand = await demands.GetAsync(id, ct) ?? throw new InvalidProductionDemandException("Backfill entry does not exist.");
        if (demand.Mode != DemandMode.Backfill || demand.State == DemandState.Removed)
            throw new InvalidProductionDemandException("This entry is not active backfill.");
        if (action == "Refresh")
        {
            // Keep old requirements for job provenance, but only the new recipe participates in balancing.
            var materializer = Materializer();
            var exploded = await materializer.ExplodeAsync([demand], ct);
            foreach (var requirement in await requirements.ListByRunLineIdAsync(id, ct)) requirement.RetireRecipe();
            demand.RefreshRecipe();
            await CancelUnsentAsync(id, ct);
            await demands.FlushAsync(ct);
            await materializer.StageAsync([demand], exploded, ct);
        }
        else if (action == "Configure")
        {
            if (printerCap is null or < 1) throw new InvalidProductionDemandException("Printer limit must be at least one.");
            if (pinnedPrinterId is { } printerId && await printers.GetByIdAsync(printerId, ct) is null)
                throw new OpenFarm.Application.Printers.PrinterNotFoundException(printerId);
            foreach (var requirement in await requirements.ListByRunLineIdAsync(id, ct))
                foreach (var job in await jobs.ListByPartRequirementIdAsync(requirement.Id, ct))
                    if (job.Status == PrintJobStatus.Queued && job.DispatchedAtUtc is null)
                    {
                        if (pinnedPrinterId is null) job.Unpin(); else job.Pin(pinnedPrinterId);
                    }
            demand.ConfigureAssignment(pinnedPrinterId);
            demand.SetBackfillLimits(printerCap.Value, demand.BackfillCumulativeUnitCeiling);
        }
        else
        {
            var state = action switch
            {
                "Enable" => DemandState.Enabled,
                "Pause" => DemandState.Paused,
                "Remove" => DemandState.Removed,
                _ => throw new InvalidProductionDemandException("Unknown backfill action.")
            };
            demand.SetState(state);
            if (state == DemandState.Removed) await CancelUnsentAsync(id, ct);
        }
        await demands.SaveAsync(ct);
        await publisher.DemandChangedAsync(id, ct);
        await dispatch.RequestIdlePrintersAsync(ct);
        return demand;
    }

    private async Task CancelUnsentAsync(Guid id, CancellationToken ct)
    {
        foreach (var requirement in await requirements.ListByRunLineIdAsync(id, ct))
            foreach (var job in await jobs.ListByPartRequirementIdAsync(requirement.Id, ct))
                if (job.Status == PrintJobStatus.Queued && job.DispatchedAtUtc is null) job.MarkCancelled();
    }
}
