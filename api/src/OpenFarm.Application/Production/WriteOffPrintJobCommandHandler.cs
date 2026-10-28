using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record WriteOffPrintJobCommand(Guid JobId);

/// <summary>
/// FR-025's second remedy: accept a lost plate of a finite requirement instead of reprinting it. Works without a
/// snapshot, so a plate that can no longer be requeued never holds its run open; may complete the run.
/// </summary>
public sealed class WriteOffPrintJobCommandHandler(
    IPrintJobRepository jobRepository,
    IPartRequirementRepository requirementRepository,
    ProductionRunCompletionService completion,
    IProductionEventPublisher publisher)
    : ICommandHandler<WriteOffPrintJobCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(WriteOffPrintJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.JobId);
        var requirement = await requirementRepository.GetByIdAsync(job.PartRequirementId, cancellationToken);
        if (requirement?.Quantity is null)
            throw new PrintJobNotEligibleForWriteOffException(job.Id);

        job.WriteOff();
        await jobRepository.SaveChangesAsync(cancellationToken);
        await publisher.PrintJobStatusChangedAsync(job.Id, job.Status, cancellationToken);
        await completion.TryCompleteRunForRequirementAsync(job.PartRequirementId, cancellationToken);
        return job;
    }
}
