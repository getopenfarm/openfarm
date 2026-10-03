using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// FR-024a/research.md R12: for a live-integrated printer, sends <see cref="IPrinterConnector.PauseJobAsync"/>
/// and returns without touching <see cref="PrintJob.Status"/> — only <c>IJobTelemetrySync</c>, once
/// the printer confirms, ever writes it. For a manual-brand job (FR-024b), applies the change directly.
/// </summary>
public sealed class PausePrintJobCommandHandler(
    ProductionRemoteControlResolver resolver, IPrinterConnectorFactory connectorFactory,
    IPrintJobRepository jobRepository, IProductionEventPublisher publisher) : ICommandHandler<PausePrintJobCommand, PrintJob>
{
    private static readonly PrintJobStatus[] EligibleStatuses = [PrintJobStatus.Printing];

    public async Task<PrintJob> HandleAsync(PausePrintJobCommand command, CancellationToken cancellationToken)
    {
        var (job, liveIntegratedPrinter, details) = await resolver.ResolveAsync(command.JobId, EligibleStatuses, cancellationToken);

        if (liveIntegratedPrinter is null)
        {
            job.MarkPaused();
            await jobRepository.SaveChangesAsync(cancellationToken);
            await publisher.PrintJobStatusChangedAsync(job.OrganizationId, job.Id, job.Status, cancellationToken);
            return job;
        }

        var connector = connectorFactory.CreateConnector(details!);
        await connector.PauseJobAsync(details!, cancellationToken);
        return job;
    }
}
