using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed class GetQueueJobsQueryHandler(
    IPrintJobRepository jobRepository,
    IPartRequirementRepository requirementRepository,
    IPartRepository partRepository,
    IProductionRunRepository runRepository,
    IPrinterRepository printerRepository,
    ProductionRunDisplayLabelResolver labelResolver,
    IPrintJobEligibilityQuery? eligibilityQuery = null) : IQueryHandler<GetQueueJobsQuery, QueueJobsPage>
{
    public async Task<QueueJobsPage> HandleAsync(GetQueueJobsQuery query, CancellationToken cancellationToken)
    {
        var (jobs, totalCount) = await jobRepository.ListQueueAsync(query.BackfillOnly, query.Page, query.PageSize, cancellationToken);

        var eligibleIds = eligibilityQuery is null ? null : await eligibilityQuery.GetEligibleJobIdsAsync(jobs, cancellationToken);
        var runLabelCache = new Dictionary<Guid, (Guid RunId, string DisplayLabel)>();
        var printerStatusCache = new Dictionary<Guid, PrinterStatus?>();
        var items = new List<QueueJobRow>();

        foreach (var job in jobs)
        {
            var requirement = await requirementRepository.GetByIdAsync(job.PartRequirementId, cancellationToken);
            if (requirement is null)
            {
                continue;
            }

            var part = await partRepository.GetByIdAsync(requirement.PartId, cancellationToken);

            if (!runLabelCache.TryGetValue(requirement.RunLineId, out var runInfo))
            {
                var run = await runRepository.GetByRunLineIdAsync(requirement.RunLineId, cancellationToken);
                var displayLabel = run is null ? "Unknown run" : await labelResolver.ResolveAsync(run, cancellationToken);
                runInfo = (run?.Id ?? Guid.Empty, displayLabel);
                runLabelCache[requirement.RunLineId] = runInfo;
            }

            // FR-007c — blocked iff Pinned and the target printer isn't currently Idle (offline,
            // maintenance/AwaitingClear, or missing entirely).
            var isBlocked = false;
            if (job.AssignmentMode == AssignmentMode.Pinned && job.PinnedPrinterId is { } pinnedPrinterId)
            {
                if (!printerStatusCache.TryGetValue(pinnedPrinterId, out var status))
                {
                    var printer = await printerRepository.GetByIdAsync(pinnedPrinterId, cancellationToken);
                    status = printer?.Status;
                    printerStatusCache[pinnedPrinterId] = status;
                }

                isBlocked = status != PrinterStatus.Idle;
            }

            if (eligibleIds is not null) isBlocked = job.Status == PrintJobStatus.Queued && job.DispatchedAtUtc is null && !eligibleIds.Contains(job.Id);

            items.Add(new QueueJobRow(
                job.Id, job.QueuePosition, job.Status, job.AssignmentMode, job.PinnedPrinterId, job.DispatchedPrinterId,
                isBlocked, requirement.PartId, part?.Name ?? "Unknown part", job.PlateQuantity,
                requirement.RunLineId, runInfo.RunId, runInfo.DisplayLabel,
                isBlocked && eligibilityQuery is not null ? await eligibilityQuery.GetBlockingReasonAsync(job, cancellationToken) : null));
        }

        return new QueueJobsPage(items, totalCount);
    }
}
