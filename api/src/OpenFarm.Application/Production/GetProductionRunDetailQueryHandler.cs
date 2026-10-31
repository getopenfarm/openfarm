using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed class GetProductionRunDetailQueryHandler(
    IProductionRunRepository runRepository,
    IPartRequirementRepository requirementRepository,
    IPrintJobRepository jobRepository,
    IPartRepository partRepository,
    IAssemblyRepository assemblyRepository,
    ProductionRunDisplayLabelResolver labelResolver,
    IPrintJobEligibilityQuery eligibilityQuery) : IQueryHandler<GetProductionRunDetailQuery, RunDetailView?>
{
    public async Task<RunDetailView?> HandleAsync(GetProductionRunDetailQuery query, CancellationToken cancellationToken)
    {
        var run = await runRepository.GetByIdAsync(query.RunId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var lines = new List<RunLineSummary>();
        foreach (var line in run.Lines)
        {
            var targetName = line.TargetName ?? (line.TargetKind == RunLineTargetKind.Part
                ? (await partRepository.GetByIdAsync(line.TargetId, cancellationToken))?.Name ?? "Unknown part"
                : (await assemblyRepository.GetByIdAsync(line.TargetId, cancellationToken))?.Name ?? "Unknown assembly");

            var requirements = new List<PartRequirementSummary>();
            var cumulativeProducedUnits = 0;
            var revisionHashes = new HashSet<string>();
            foreach (var requirement in await requirementRepository.ListByRunLineIdAsync(line.Id, cancellationToken))
            {
                var libraryPart = await partRepository.GetByIdAsync(requirement.PartId, cancellationToken);
                var jobs = await jobRepository.ListByPartRequirementIdAsync(requirement.Id, cancellationToken);
                var part = jobs.Select(j => j.GetProductionPart()).FirstOrDefault(p => p is not null) ?? libraryPart;
                var completedUnits = jobs.Sum(j => j.ProducedUnits);

                requirements.Add(new PartRequirementSummary(
                    requirement.Id, requirement.PartId, requirement.PartName ?? part?.Name ?? "Unknown part", requirement.Quantity,
                    libraryPart is not null, (jobs.Count > 0 ? jobs[0].PlateQuantity : part?.UnitsPerPrint ?? 1), completedUnits,
                    jobs.Sum(j => j.WrittenOffUnits), PlateCounts.Of(jobs),
                    part?.EstimatedPrintTimeMinutes, part?.EstimatedFilamentWeightGrams,
                    await NextPlateBlockingReasonAsync(jobs, cancellationToken),
                    jobs.Select(j => new RunJobSummary(
                        j.Id, j.Status, j.PrinterId, j.StartedAtUtc, j.EndedAtUtc,
                        j.Status == PrintJobStatus.Queued ? j.LastBlockReason : null, j.RequeuedAsJobId, j.WrittenOffAtUtc is not null,
                        requirement.Quantity is not null && j.IsUnresolvedLoss,
                        requirement.Quantity is not null && j.IsUnresolvedLoss && run.IsOpen && j.ProductionSnapshotJson is not null && !(j.Status == PrintJobStatus.Completed && j.ObjectExecution is { Observed: false }),
                        j.EffectiveTargetUnits, j.ProducedUnits, j.MissingUnits, j.ObjectExecution?.Observed ?? true)).ToList()));

                // FR-006e — coarse cumulative count, research.md R4's own occupancy/ceiling formula.
                cumulativeProducedUnits += completedUnits;

                // FR-009e — computed, never stored: distinct revisions across every requirement on this line.
                foreach (var job in jobs.Where(j => j.StartedAtUtc is not null))
                {
                    revisionHashes.Add(job.FileRevisionHash);
                }
            }

            lines.Add(new RunLineSummary(
                line.Id, line.TargetKind, line.TargetId, targetName, line.Quantity, line.RevisionMode, line.DefaultPinnedPrinterId,
                line.BackfillPrinterCap, line.BackfillCumulativeUnitCeiling, cumulativeProducedUnits,
                revisionHashes.Count > 1, requirements, line.QuantityBasis));
        }

        return new RunDetailView(
            run.Id, await labelResolver.ResolveAsync(run, cancellationToken), run.Name, run.DueDate, run.Notes, run.Status, run.CreatedAtUtc, lines);
    }

    /// <summary>One eligibility check per requirement, on its next waiting plate: the plates of one part share their needs.</summary>
    private async Task<string?> NextPlateBlockingReasonAsync(IReadOnlyList<PrintJob> jobs, CancellationToken cancellationToken)
    {
        var next = jobs.FirstOrDefault(j => j.Status == PrintJobStatus.Queued && j.DispatchedAtUtc is null);
        if (next is null || (await eligibilityQuery.GetEligibleJobIdsAsync([next], cancellationToken)).Contains(next.Id))
        {
            return null;
        }

        return await eligibilityQuery.GetBlockingReasonAsync(next, cancellationToken) ?? "No printer matches.";
    }
}
