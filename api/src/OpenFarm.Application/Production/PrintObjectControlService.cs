using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed class PrintObjectControlService(IPrintObjectManifestReader reader, IPartRepository parts,
    IPrintJobRepository jobs, IProductionDemandRepository transactions, ProductionRemoteControlResolver remote,
    IPrinterConnectorFactory connectors, ProductionRunCompletionService completion, IProductionEventPublisher publisher, IPrinterRepository printers,
    IProductionDispatcher dispatcher)
{
    public async Task<PrintObjectManifest> ReadPartAsync(Guid id, CancellationToken ct)
    {
        var part = await parts.GetByIdAsync(id, ct) ?? throw new OpenFarm.Domain.Parts.PartNotFoundException(id);
        return await reader.ReadAsync(part, ct);
    }

    public async Task<PrintObjectManifest> SaveGroupsAsync(Guid id, string revision, IReadOnlyList<PrintUnit> units, CancellationToken ct)
    {
        await using var locked = await transactions.LockAsync(id, ct);
        var part = await parts.GetByIdAsync(id, ct) ?? throw new OpenFarm.Domain.Parts.PartNotFoundException(id);
        if (part.RevisionHash != revision) throw new ProductionPlanConflictException();
        // Read current file objects, not the previous grouping which an operator may be repairing.
        var manifest = await reader.ReadAsync(part, ct);
        if (units.Count != manifest.Units.Count || units.Any(u => u is null || u.ObjectIds is null || u.ObjectIds.Count != 1)
            || !units.Zip(manifest.Units).All(pair => pair.First.Id == pair.Second.Id && pair.First.ObjectIds.SequenceEqual(pair.Second.ObjectIds)))
            throw new InvalidProductionDemandException("Each printable object is one unit. Custom object grouping is no longer supported.");
        try { manifest.Validate(part.UnitsPerPrint); }
        catch (InvalidOperationException ex) { throw new InvalidProductionDemandException(ex.Message); }
        await transactions.SaveAsync(ct);
        return manifest;
    }

    public async Task<PrintObjectExecution?> ReadJobAsync(Guid id, CancellationToken ct)
    {
        var job = await jobs.GetByIdAsync(id, ct) ?? throw new PrintJobNotFoundException(id);
        if (job.ObjectExecution is not { } execution) return null;
        var printer = job.PrinterId is { } printerId ? await printers.GetByIdAsync(printerId, ct) : null;
        if (printer is null) return execution with { CanSkipObjects = false };
        if (!MatchesExecution(job, printer))
            return execution with { CanSkipObjects = false, SkipUnavailableReason = "Waiting for the printer to identify this job's file." };
        return execution with { CanSkipObjects = true };
    }

    public async Task<PrintObjectExecution> ConfirmNotStartedAsync(Guid id, string artifactHash, CancellationToken ct)
    {
        await using var locked = await transactions.LockAsync(id, ct);
        var (job, printer, _) = await remote.ResolveAsync(id, [PrintJobStatus.Queued], ct);
        var execution = job.ObjectExecution ?? throw new InvalidProductionDemandException("This job has no execution artifact.");
        if (!execution.StartRequested || job.DispatchedAtUtc is null || execution.ArtifactHash != artifactHash
            || printer?.Status != OpenFarm.Domain.Printers.PrinterStatus.Idle)
            throw new InvalidProductionDemandException("Only an unconfirmed start on an idle printer can be resolved this way.");
        // Not a user cancel: the send failed and the plate is a loss to requeue or write off, like any other failed send.
        job.FailSend("Send failed: the operator confirmed the print did not start.", DateTimeOffset.UtcNow);
        await transactions.SaveAsync(ct);
        await publisher.PrintJobStatusChangedAsync(job.Id, job.Status, ct);
        dispatcher.RequestDispatchAttempt(printer.Id);
        return execution;
    }

    public async Task<PrintObjectExecution> ReconcileAsync(Guid id, string artifactHash, IReadOnlyList<string> producedUnitIds, CancellationToken ct)
    {
        await using var locked = await transactions.LockAsync(id, ct);
        var job = await jobs.GetByIdAsync(id, ct) ?? throw new PrintJobNotFoundException(id);
        var execution = job.ObjectExecution ?? throw new InvalidProductionDemandException("This job has no object mapping.");
        if (job.Status != PrintJobStatus.Completed || execution.Observed || job.RequeuedAsJobId is not null || job.WrittenOffAtUtc is not null)
            throw new InvalidProductionDemandException("Only an unresolved completed print can have its output confirmed.");
        if (execution.ArtifactHash != artifactHash || artifactHash.Length == 0) throw new ProductionPlanConflictException();
        if (producedUnitIds.Distinct().Count() != producedUnitIds.Count || producedUnitIds.Any(u => !execution.SelectedUnitIds.Contains(u)))
            throw new InvalidProductionDemandException("Confirm complete units selected for this print.");
        execution = execution with
        {
            Observed = true,
            RequestedObjectIds = [],
            RequestError = null,
            ConfirmedObjectIds = execution.Manifest.Units.Where(u => !producedUnitIds.Contains(u.Id)).SelectMany(u => u.ObjectIds).ToArray()
        };
        job.RecordObjectExecution(execution);
        await transactions.SaveAsync(ct);
        await publisher.PrintJobStatusChangedAsync(job.Id, job.Status, ct);
        await completion.TryCompleteRunForRequirementAsync(job.PartRequirementId, ct);
        return execution;
    }

    public async Task<PrintObjectExecution> SkipAsync(Guid id, string artifactHash, IReadOnlyList<string> unitIds, CancellationToken ct)
    {
        await using var locked = await transactions.LockAsync(id, ct);
        var (job, printer, details) = await remote.ResolveAsync(id, [PrintJobStatus.Printing, PrintJobStatus.Paused], ct);
        if (printer is null || details is null) throw new InvalidProductionDemandException("Object skipping needs a live-integrated printer.");
        var execution = job.ObjectExecution ?? throw new InvalidProductionDemandException("This job has no verified object mapping.");
        if (!MatchesExecution(job, printer)) throw new InvalidProductionDemandException("The printer has not identified this job's execution file. Refresh its status before skipping objects.");
        if (execution.ArtifactHash is null || execution.ArtifactHash != artifactHash) throw new ProductionPlanConflictException();
        if ((await jobs.GetActiveJobByDispatchedPrinterIdAsync(printer.Id, ct))?.Id != id)
            throw new ProductionPlanConflictException();
        if (unitIds.Count == 0 || unitIds.Distinct().Count() != unitIds.Count || unitIds.Any(u => !execution.SelectedUnitIds.Contains(u)))
            throw new InvalidProductionDemandException("Select complete units belonging to this job.");
        var requested = execution.RequestedObjectIds.Union(execution.Manifest.Units.Where(u => unitIds.Contains(u.Id)).SelectMany(u => u.ObjectIds)).ToArray();
        var all = execution.PlannedExclusions.Union(execution.ConfirmedObjectIds).Union(requested).ToArray();
        if (execution.Manifest.Units.All(u => u.ObjectIds.Any(all.Contains)))
            throw new InvalidProductionDemandException("Use Cancel print to stop all remaining units.");
        if (requested.All(execution.RequestedObjectIds.Contains) && execution.RequestError is null) return execution;
        if (!execution.Observed && execution.RequestedObjectIds.Count > 0 && execution.RequestError is null)
            throw new InvalidProductionDemandException("Wait for the pending skip request to be confirmed.");
        execution = execution with { RequestedObjectIds = requested, Observed = false, RequestError = null };
        job.RecordObjectExecution(execution);
        await transactions.SaveAsync(ct); // Durable intent before an irreversible printer command.
        await publisher.PrintJobStatusChangedAsync(job.Id, job.Status, ct);
        try { await connectors.CreateConnector(details).SkipObjectsAsync(details, all, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            execution = execution with { RequestError = "Skip request was not confirmed. Retry the same selection or check the printer." };
            job.RecordObjectExecution(execution);
            await jobs.SaveChangesAsync(ct);
            await publisher.PrintJobStatusChangedAsync(job.Id, job.Status, ct);
        }
        return execution;
    }
    private static bool MatchesExecution(PrintJob job, OpenFarm.Domain.Printers.Printer printer) =>
        job.ObjectExecution?.ArtifactName is not null &&
        (printer.CurrentGcodeFile?.Contains(job.Id.ToString("N"), StringComparison.Ordinal) == true
            || printer.CurrentJobName?.Contains(job.Id.ToString("N"), StringComparison.Ordinal) == true);

}
