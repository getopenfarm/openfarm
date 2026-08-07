using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>Reverts to Unassigned, eligible for ordinary dispatch matching again. 404 on missing job, 409 if not Queued (PrintJob.Unpin's own EnsureQueued guard).</summary>
public sealed class UnpinPrintJobCommandHandler(IPrintJobRepository jobRepository) : ICommandHandler<UnpinPrintJobCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(UnpinPrintJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.JobId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.JobId);

        job.Unpin();
        await jobRepository.SaveChangesAsync(cancellationToken);

        return job;
    }
}
