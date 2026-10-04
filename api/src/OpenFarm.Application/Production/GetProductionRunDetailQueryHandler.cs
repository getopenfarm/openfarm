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
    IAssemblyRepository assemblyRepository) : IQueryHandler<GetProductionRunDetailQuery, RunDetailView?>
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
            var targetName = line.TargetKind == RunLineTargetKind.Part
                ? (await partRepository.GetByIdAsync(line.TargetId, cancellationToken))?.Name ?? "Unknown part"
                : (await assemblyRepository.GetByIdAsync(line.TargetId, cancellationToken))?.Name ?? "Unknown assembly";

            var requirements = new List<PartRequirementSummary>();
            var cumulativeProducedUnits = 0;
            var revisionHashes = new HashSet<string>();
            foreach (var requirement in await requirementRepository.ListByRunLineIdAsync(line.Id, cancellationToken))
            {
                var part = await partRepository.GetByIdAsync(requirement.PartId, cancellationToken);
                var jobs = await jobRepository.ListByPartRequirementIdAsync(requirement.Id, cancellationToken);

                // research.md R13 — a plain filter, never a stored/computed aggregate.
                var jobsNeedingRequeue = (requirement.Quantity is null ? [] : jobs)
                    .Where(j => j.Status is PrintJobStatus.Cancelled or PrintJobStatus.Failed && j.RequeuedAsJobId is null)
                    .Select(j => j.Id)
                    .ToList();

                requirements.Add(new PartRequirementSummary(
                    requirement.Id, requirement.PartId, part?.Name ?? "Unknown part", requirement.Quantity,
                    jobs.Count, jobs.Count(j => j.Status == PrintJobStatus.Completed), jobsNeedingRequeue));

                // FR-006e — coarse cumulative count, research.md R4's own occupancy/ceiling formula.
                cumulativeProducedUnits += jobs.Where(j => j.Status == PrintJobStatus.Completed).Sum(j => j.PlateQuantity);

                // FR-009e — computed, never stored: distinct revisions across every requirement on this line.
                foreach (var job in jobs.Where(j => j.StartedAtUtc is not null))
                {
                    revisionHashes.Add(job.FileRevisionHash);
                }
            }

            lines.Add(new RunLineSummary(
                line.Id, line.TargetKind, line.TargetId, targetName, line.Quantity, line.RevisionMode,
                line.BackfillPrinterCap, line.BackfillCumulativeUnitCeiling, cumulativeProducedUnits,
                revisionHashes.Count > 1, requirements));
        }

        return new RunDetailView(run.Id, run.Name, run.DueDate, run.Priority, run.Notes, run.Status, lines);
    }
}
