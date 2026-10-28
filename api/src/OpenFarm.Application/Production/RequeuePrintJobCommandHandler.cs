using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// FR-025a/research.md R13: reprints a lost plate — only a Cancelled/Failed job of a finite requirement on an open run,
/// not already requeued or written off. The one replacement takes the lost job's own queue position, so it retries
/// inside its run's block instead of behind runs queued later, and keeps its pin only while that printer exists.
/// </summary>
public sealed class RequeuePrintJobCommandHandler(
    IPrintJobRepository jobRepository,
    IPartRequirementRepository requirementRepository,
    IProductionRunRepository runRepository,
    IPrinterRepository printerRepository,
    ProductionDispatchRequestService? dispatchRequests = null)
    : ICommandHandler<RequeuePrintJobCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(RequeuePrintJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.JobId);

        if (job.SnapshotUnavailableAtUtc is not null)
            throw new ProductionSnapshotUnavailableException(job.Id, job.SnapshotRecoveryReason);

        if (job.RequeuedAsJobId is not null)
        {
            throw new PrintJobAlreadyRequeuedException(job.Id);
        }

        if (!job.IsUnresolvedLoss)
        {
            throw new PrintJobNotEligibleForRequeueException(job.Id);
        }

        var requirement = await requirementRepository.GetByIdAsync(job.PartRequirementId, cancellationToken)
            ?? throw new PrintJobNotEligibleForRequeueException(job.Id);

        if (requirement.Quantity is null)
        {
            // FR-025d — an unlimited/backfill line's job has no fixed slot to "lose".
            throw new PrintJobNotEligibleForRequeueException(job.Id);
        }

        var run = await runRepository.GetByRunLineIdAsync(requirement.RunLineId, cancellationToken);
        if (run is { IsOpen: false })
            throw new ProductionRunNotOpenException(run.Id);

        if (job.ProductionSnapshotJson is null)
            throw new ProductionSnapshotUnavailableException(job.Id, job.SnapshotRecoveryReason);

        var pinnedPrinterId = job.AssignmentMode == AssignmentMode.Pinned && job.PinnedPrinterId is { } printerId
            && await printerRepository.GetByIdAsync(printerId, cancellationToken) is not null ? job.PinnedPrinterId : null;
        var filamentSpecs = job.Filaments
            .Select(c => c.Requirement)
            .ToList();

        var replacement = PrintJob.Create(
            job.Name, requirement.Id, job.QueuePosition, job.PlateQuantity,
            pinnedPrinterId is null ? AssignmentMode.Unassigned : AssignmentMode.Pinned, pinnedPrinterId,
            job.FileRevisionHash, job.FileRevisionNumber, filamentSpecs);

        replacement.CopyProductionSnapshot(job);
        if (job.HasFilamentRequirementOverride) replacement.SetFilamentRequirements(filamentSpecs);
        await jobRepository.AddAsync(replacement, cancellationToken);
        job.MarkRequeuedAs(replacement.Id);
        await jobRepository.SaveChangesAsync(cancellationToken);

        if (dispatchRequests is not null) await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return replacement;
    }
}
