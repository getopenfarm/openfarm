using System.ComponentModel;
using System.IdentityModel.Tokens.Jwt;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.Maintenance;
using OpenFarm.Api.Endpoints.Notifications;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Application.Common;
using OpenFarm.Application.Maintenance;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;

namespace OpenFarm.Api.Mcp.Tools;

/// <param name="Confirmed">False until the printer reports the change; read the job again instead of repeating the command.</param>
public sealed record PrinterCommandResult(PrintJobResponse Job, bool Confirmed, string? Note);

public static class WriteTools
{
    private const string Unconfirmed = "The printer has not reported the change yet. Read the job again to confirm; do not repeat the command.";
    private const string Confirm = "Before calling, present the job, printer and progress and obtain reviewed confirmation.";

    public static readonly McpToolSpec[] All =
    [
        McpTools.Write("reorder_print_job", "Reorder print job",
            "Moves a queued job to just after another queued job, or to the front. This changes what prints next.",
            destructive: true, idempotent: true, openWorld: false,
            async (RequestContext<CallToolRequestParams> request,
                [Description("Job to move")] Guid jobId,
                [Description("Queued job to place it after; omit to move it to the front")] Guid? afterJobId = null,
                CancellationToken ct = default) =>
                await request.WriteAsync(new ReorderPrintJobRequest { Id = jobId, AfterJobId = afterJobId }, async () =>
                {
                    var job = await request.Service<ICommandHandler<ReorderPrintJobCommand, PrintJob>>()
                        .HandleAsync(new ReorderPrintJobCommand(jobId, afterJobId), ct);
                    return new ReorderPrintJobResponse { JobId = job.Id, QueuePosition = job.QueuePosition };
                }, ct),
            "print-jobs:reorder"),
        McpTools.Write("pin_print_job", "Pin print job",
            "Makes a queued job wait for one printer.",
            destructive: true, idempotent: true, openWorld: false,
            async (RequestContext<CallToolRequestParams> request, [Description("Job to pin")] Guid jobId,
                [Description("Printer the job must print on")] Guid printerId, CancellationToken ct = default) =>
                await request.WriteAsync(new PinPrintJobRequest { Id = jobId, PrinterId = printerId }, async () =>
                    await DetailAsync(request, await request.Service<ICommandHandler<PinPrintJobCommand, PrintJob>>()
                        .HandleAsync(new PinPrintJobCommand(jobId, printerId), ct), ct), ct),
            "print-jobs:pin"),
        McpTools.Write("unpin_print_job", "Unpin print job",
            "Lets a queued job print on any matching printer again.",
            destructive: true, idempotent: true, openWorld: false,
            async (RequestContext<CallToolRequestParams> request, [Description("Job to unpin")] Guid jobId, CancellationToken ct = default) =>
                await request.WriteAsync(new UnpinPrintJobRequest { Id = jobId }, async () =>
                    await DetailAsync(request, await request.Service<ICommandHandler<UnpinPrintJobCommand, PrintJob>>()
                        .HandleAsync(new UnpinPrintJobCommand(jobId), ct), ct), ct),
            "print-jobs:pin"),
        McpTools.Write("requeue_print_job", "Requeue print job",
            "Queues a replacement for a failed or cancelled job. An idle printer may start it on its own. " + Confirm,
            destructive: false, idempotent: false, openWorld: true,
            async (RequestContext<CallToolRequestParams> request, [Description("Failed or cancelled job")] Guid jobId, CancellationToken ct = default) =>
                await request.WriteAsync(new RequeuePrintJobRequest { Id = jobId }, async () =>
                    await DetailAsync(request, await request.Service<ICommandHandler<RequeuePrintJobCommand, PrintJob>>()
                        .HandleAsync(new RequeuePrintJobCommand(jobId), ct), ct), ct),
            "print-jobs:requeue"),
        McpTools.Write("dismiss_notification", "Dismiss notification",
            "Dismisses a filament follow-up notification. The print's undeducted filament stays as it is.",
            destructive: true, idempotent: true, openWorld: false,
            async (RequestContext<CallToolRequestParams> request, [Description("Print id from list_notifications")] Guid printId, CancellationToken ct = default) =>
                await request.WriteAsync(new DismissNotificationRequest { Id = printId }, async () =>
                    new DismissNotificationResponse
                    {
                        Id = await request.Service<ICommandHandler<DismissNotificationCommand, Guid>>()
                            .HandleAsync(new DismissNotificationCommand(printId), ct),
                    }, ct),
            "print-jobs:update-status"),
        McpTools.Write("complete_maintenance", "Mark maintenance done",
            "Marks maintenance enrollments done, all or none. A failure lists why each enrollment failed.",
            destructive: false, idempotent: false, openWorld: false,
            async (RequestContext<CallToolRequestParams> request,
                [Description("Enrollment ids from list_maintenance")] Guid[] enrollmentIds,
                [Description("When the work was done (UTC); default now, not before the cycle start")] DateTimeOffset? performedAtUtc = null,
                [Description("Notes")] string? notes = null,
                [Description("Who did the work, on top of the signed-in user")] string? performedByNote = null,
                [Description("Cost in the farm currency, 0 or more")] decimal? cost = null,
                CancellationToken ct = default) =>
            {
                var dto = new CompleteMaintenanceRequest
                {
                    EnrollmentIds = [.. enrollmentIds],
                    PerformedAtUtc = performedAtUtc,
                    Notes = notes,
                    PerformedByNote = performedByNote,
                    Cost = cost,
                };
                var userId = Guid.Parse(request.Service<IHttpContextAccessor>().HttpContext!.User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
                return await request.WriteAsync(dto, async () => MaintenanceChangeResponse.ForPrinters(
                    await request.Service<MaintenanceCommandHandlers>().HandleAsync(new CompleteMaintenanceCommand(
                        dto.EnrollmentIds, performedAtUtc, userId, performedByNote, notes, cost), ct)), ct);
            },
            "maintenance:complete"),
        McpTools.Write("pause_print_job", "Pause print",
            "Pauses a printing job on its printer at once. " + Confirm,
            destructive: true, idempotent: false, openWorld: true,
            async (RequestContext<CallToolRequestParams> request, [Description("Printing job")] Guid jobId, CancellationToken ct = default) =>
                await request.WriteAsync(new PausePrintJobRequest { Id = jobId }, async () =>
                {
                    var job = await request.Service<ICommandHandler<PausePrintJobCommand, PrintJob>>()
                        .HandleAsync(new PausePrintJobCommand(jobId), ct);
                    var confirmed = job.Status == PrintJobStatus.Paused;
                    return new PrinterCommandResult(await DetailAsync(request, job, ct), confirmed, confirmed ? null : Unconfirmed);
                }, ct),
            "print-jobs:remote-control"),
        McpTools.Write("cancel_print_job", "Cancel print job",
            "Removes a queued job, or stops a starting or printing job on the printer. A stopped print cannot be resumed. "
            + Confirm + " Pass the phase you read as expectedPhase.",
            destructive: true, idempotent: false, openWorld: true,
            async (RequestContext<CallToolRequestParams> request, [Description("Job to cancel")] Guid jobId,
                [Description("The job's phase when you read it: Queued, Starting, Printing or Paused")] string expectedPhase,
                CancellationToken ct = default) =>
            {
                if (!Enum.TryParse<PrintJobPhase>(expectedPhase, ignoreCase: true, out var expected) || !Enum.IsDefined(expected))
                    throw new ArgumentException("expectedPhase must be Queued, Starting, Printing or Paused.");
                return await request.WriteAsync(new CancelPrintJobRequest { Id = jobId }, async () =>
                {
                    var job = await request.Service<ICommandHandler<CancelPrintJobCommand, PrintJob>>()
                        .HandleAsync(new CancelPrintJobCommand(jobId, expected), ct);
                    var confirmed = job.Status == PrintJobStatus.Cancelled;
                    return new PrinterCommandResult(await DetailAsync(request, job, ct), confirmed, confirmed ? null : Unconfirmed);
                }, ct);
            },
            "print-jobs:cancel"),
    ];

    private static async Task<PrintJobResponse> DetailAsync(RequestContext<CallToolRequestParams> request, PrintJob job, CancellationToken ct)
    {
        var detail = PrintJobResponse.FromDetail((await request.Service<IQueryHandler<GetPrintJobQuery, PrintJobDetail?>>()
            .HandleAsync(new GetPrintJobQuery(job.Id), ct))!);
        detail.ThumbnailBase64 = null;
        return detail;
    }
}
