using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record SetPrintJobFilamentRequirementsCommand(Guid JobId, IReadOnlyList<JobFilamentRequirementSpec> Requirements);
public sealed record RecordPrintJobFilamentConsumptionCommand(Guid JobId, IReadOnlyList<FilamentConsumptionLineInput> Lines);

public sealed class SetPrintJobFilamentRequirementsCommandHandler(IPrintJobRepository repository, ProductionDispatchRequestService dispatchRequests)
    : ICommandHandler<SetPrintJobFilamentRequirementsCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(SetPrintJobFilamentRequirementsCommand command, CancellationToken cancellationToken)
    {
        var job = await repository.GetByIdAsync(command.JobId, cancellationToken) ?? throw new PrintJobNotFoundException(command.JobId);
        job.SetFilamentRequirements(command.Requirements);
        repository.TrackNewFilamentConsumption(job.FilamentConsumption);
        await repository.SaveChangesAsync(cancellationToken);
        await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return job;
    }
}

public sealed class RecordPrintJobFilamentConsumptionCommandHandler(IPrintJobRepository repository, ISpoolRepository spools)
    : ICommandHandler<RecordPrintJobFilamentConsumptionCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(RecordPrintJobFilamentConsumptionCommand command, CancellationToken cancellationToken)
    {
        var job = await repository.GetByIdAsync(command.JobId, cancellationToken) ?? throw new PrintJobNotFoundException(command.JobId);
        if (job.Status is not (PrintJobStatus.Completed or PrintJobStatus.Cancelled or PrintJobStatus.Failed))
            throw new JobNotEligibleForRemoteControlException(job.Id);
        await PrintJobFilamentConsumptionRecorder.RecordAsync(job, command.Lines, spools, cancellationToken, repository);
        await repository.SaveChangesAsync(cancellationToken);
        return job;
    }
}
