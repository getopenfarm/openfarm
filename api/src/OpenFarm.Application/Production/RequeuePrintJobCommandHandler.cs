using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// FR-025a/research.md R13: the one remedy for a shortfall — only valid on a Cancelled/Failed job,
/// not already requeued, belonging to a finite requirement (FR-025d). Creates exactly one new job for
/// the same requirement via the ordinary creation rules (T050), appended to the queue's end, and links
/// it back via <see cref="PrintJob.MarkRequeuedAs"/>.
/// </summary>
public sealed class RequeuePrintJobCommandHandler(
    IPrintJobRepository jobRepository, IPartRequirementRepository requirementRepository, IPartRepository partRepository, ProductionDispatchRequestService? dispatchRequests = null)
    : ICommandHandler<RequeuePrintJobCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(RequeuePrintJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.JobId);

        if (job.Status is not (PrintJobStatus.Cancelled or PrintJobStatus.Failed))
        {
            throw new PrintJobNotEligibleForRequeueException(job.Id);
        }

        if (job.RequeuedAsJobId is not null)
        {
            throw new PrintJobAlreadyRequeuedException(job.Id);
        }

        var requirement = await requirementRepository.GetByIdAsync(job.PartRequirementId, cancellationToken)
            ?? throw new PrintJobNotEligibleForRequeueException(job.Id);

        if (requirement.Quantity is null)
        {
            // FR-025d — an unlimited/backfill line's job has no fixed slot to "lose".
            throw new PrintJobNotEligibleForRequeueException(job.Id);
        }

        var part = await partRepository.GetByIdAsync(requirement.PartId, cancellationToken)
            ?? throw new PartNotFoundException(requirement.PartId);

        var nextQueuePosition = (await jobRepository.GetMaxQueuePositionAsync(job.OrganizationId, cancellationToken) ?? 0) + 1024;
        var filamentSpecs = part.FilamentRequirements
            .Select(r => new JobFilamentRequirementSpec(r.MaterialType, r.DetectedColorHex))
            .ToList();

        var replacement = PrintJob.Create(
            job.OrganizationId, requirement.Id, nextQueuePosition, part.UnitsPerPrint,
            job.AssignmentMode, job.PinnedPrinterId, job.FileRevisionHash, job.FileRevisionNumber, filamentSpecs);

        await jobRepository.AddAsync(replacement, cancellationToken);
        job.MarkRequeuedAs(replacement.Id);
        await jobRepository.SaveChangesAsync(cancellationToken);

        if (dispatchRequests is not null) await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return replacement;
    }
}
