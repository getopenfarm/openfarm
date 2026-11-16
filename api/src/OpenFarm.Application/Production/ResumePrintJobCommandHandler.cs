using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.AuditLog;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>Mirrors <see cref="PausePrintJobCommandHandler"/>'s request/confirm split (FR-024a/b) — resume is only valid from Paused.</summary>
public sealed class ResumePrintJobCommandHandler(
    ProductionRemoteControlResolver resolver, IPrinterConnectorFactory connectorFactory,
    IPrintJobRepository jobRepository, IProductionEventPublisher publisher, IActivityLogWriter? auditWriter = null, IAuditContextAccessor? auditContext = null) : ICommandHandler<ResumePrintJobCommand, PrintJob>
{
    private static readonly PrintJobStatus[] EligibleStatuses = [PrintJobStatus.Paused];

    public async Task<PrintJob> HandleAsync(ResumePrintJobCommand command, CancellationToken cancellationToken)
    {
        var (job, liveIntegratedPrinter, details) = await resolver.ResolveAsync(command.JobId, EligibleStatuses, cancellationToken);

        if (liveIntegratedPrinter is null)
        {
            job.MarkResumed();
            await jobRepository.SaveChangesAsync(cancellationToken);
            await publisher.PrintJobStatusChangedAsync(job.Id, job.Status, cancellationToken);
            return job;
        }

        var connector = connectorFactory.CreateConnector(details!);
        await connector.ResumeJobAsync(details!, cancellationToken);
        if (auditWriter is not null)
        {
            var actor = auditContext?.Current?.ActorId;
            await auditWriter.WriteAsync(ActivityLogEntry.Create("PrintJob", job.Id.ToString(), "PrintJobResumeRequested",
                actor, actor is null ? ActorType.System : ActorType.Operator), cancellationToken);
        }
        return job;
    }
}
