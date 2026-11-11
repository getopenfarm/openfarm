using System.ComponentModel;
using FluentValidation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;

namespace OpenFarm.Api.Mcp.Tools;

/// <summary>A print job as agents read it: names next to ids.</summary>
/// <param name="Name">The part's name, or the print's name for a print the printer started itself.</param>
/// <param name="Phase">Queued, Starting, Printing or Paused while unfinished; pass it as expectedPhase to cancel_print_job.</param>
/// <param name="Printer">The printer it was sent to, or the one it is pinned to while queued.</param>
/// <param name="Units">Units this job makes when it completes.</param>
/// <param name="LastBlockReason">Why the last attempt to send it to a matching printer failed.</param>
public sealed record McpJob(Guid JobId, string Name, string Status, string? Phase, Guid? PrinterId, string? Printer, bool Pinned,
    Guid? ProductionRunId, string? Run, int? Plates, int? Units, double? QueuePosition, string? LastBlockReason,
    DateTimeOffset? StartedAtUtc, DateTimeOffset? EndedAtUtc, bool CanRequeue);

/// <param name="Grams">Grams deducted from the spool; null until the print ends.</param>
public sealed record McpJobFilament(int ToolIndex, string? MaterialType, string? ColorHex, decimal? EstimatedGrams, decimal? Grams, Guid? SpoolId);

/// <param name="Waiting">For a queued job: why no printer has taken it yet, per printer.</param>
public sealed record McpJobDetail(Guid JobId, string Name, string Status, string? Phase, Guid? PrinterId, string? Printer, bool Pinned,
    int? Plates, int? Units, int? ProducedUnits, double? QueuePosition, string? LastBlockReason, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc, DateTimeOffset? EndedAtUtc, int? DurationMinutes, Guid? RequeuedAsJobId,
    IReadOnlyList<McpJobFilament> Filament, McpWaiting? Waiting);

/// <param name="Printers">Each printer and the checks it fails; an eligible printer is only waiting for its turn.</param>
public sealed record McpWaiting(string Summary, IReadOnlyList<McpWaitingPrinter> Printers);

public sealed record McpWaitingPrinter(string Printer, bool Eligible, IReadOnlyList<string> Failed, string? Note);

/// <param name="Confirmed">False until the printer reports the change; read the job again instead of repeating the command.</param>
public sealed record PrinterCommandResult(McpJobDetail Job, bool Confirmed, string? Note);

public sealed record McpJobsPage(IReadOnlyList<McpJob> Items, int Page, int PageSize, int TotalCount, bool HasMore, IReadOnlyDictionary<string, int> StatusCounts);

public static class QueueTools
{
    private const string Unconfirmed = "The printer has not reported the change yet. Read the job again to confirm; do not repeat the command.";
    private const string Confirm = "Before calling, present the job, printer and progress and obtain reviewed confirmation.";

    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("list_print_jobs", "List print jobs",
            "Print jobs in dispatch order with part, printer and run names, phase, units and why a waiting job was not sent, plus counts per status. "
            + "Use search to find a part's jobs. " + McpTools.PagingNote,
            async (RequestContext<CallToolRequestParams> request,
                [Description("queue (default): jobs OpenFarm sends; backfill: unlimited work; external: prints a printer started itself, newest first")] string? view = null,
                [Description("InProgress (printing or paused), Queued, Printing, Paused, Completed, Failed or Cancelled")] string? status = null,
                [Description("Matches the part name, or the print name for external")] string? search = null,
                [Description(McpTools.PageArg)] int? page = null,
                [Description(McpTools.PageSizeArg)] int? pageSize = null,
                CancellationToken ct = default) =>
            {
                new ListQueueJobsValidator().ValidateAndThrow(new ListQueueJobsRequest { Status = status });
                var (p, size) = (McpTools.Page(page), McpTools.PageSize(pageSize));
                var jobs = await request.Service<IQueryHandler<GetQueueJobsQuery, QueueJobsPage>>().HandleAsync(new GetQueueJobsQuery(p, size,
                    BackfillOnly: string.Equals(view, "backfill", StringComparison.OrdinalIgnoreCase),
                    Status: status, Search: search,
                    ExternalOnly: string.Equals(view, "external", StringComparison.OrdinalIgnoreCase)), ct);
                var printers = await PrinterNamesAsync(request, ct);
                var items = jobs.Items.Select(row => Job(row, printers)).ToList();
                return new McpJobsPage(items, p, size, jobs.TotalCount, p * size < jobs.TotalCount, jobs.StatusCounts);
            },
            "print-jobs:view"),
        McpTools.Read("get_print_job", "Get print job",
            "One print job: phase, printer, units, filament lines and timestamps. For a queued job, waiting explains per printer why it has not been sent "
            + "(nozzle, material, colour, plate, pin or the printer being busy).",
            async (RequestContext<CallToolRequestParams> request, [Description("Print job id")] Guid jobId, CancellationToken ct) =>
                await DetailAsync(request, jobId, ct, waiting: true),
            "print-jobs:view"),
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
            "Makes a queued job wait for one printer, or with no printerId lets it print on any matching printer again.",
            destructive: true, idempotent: true, openWorld: false,
            async (RequestContext<CallToolRequestParams> request, [Description("Job to pin or unpin")] Guid jobId,
                [Description("Printer the job must print on; omit to unpin")] Guid? printerId = null, CancellationToken ct = default) =>
                printerId is { } id
                    ? await request.WriteAsync(new PinPrintJobRequest { Id = jobId, PrinterId = id }, async () =>
                        await DetailAsync(request, (await request.Service<ICommandHandler<PinPrintJobCommand, PrintJob>>()
                            .HandleAsync(new PinPrintJobCommand(jobId, id), ct)).Id, ct), ct)
                    : await request.WriteAsync(new UnpinPrintJobRequest { Id = jobId }, async () =>
                        await DetailAsync(request, (await request.Service<ICommandHandler<UnpinPrintJobCommand, PrintJob>>()
                            .HandleAsync(new UnpinPrintJobCommand(jobId), ct)).Id, ct), ct),
            "print-jobs:pin"),
        McpTools.Write("requeue_print_job", "Requeue print job",
            "Queues a replacement for a failed or cancelled job and returns the replacement. An idle printer may start it on its own. " + Confirm,
            destructive: false, idempotent: false, openWorld: true,
            async (RequestContext<CallToolRequestParams> request, [Description("Failed or cancelled job")] Guid jobId, CancellationToken ct = default) =>
                await DetailAsync(request, (await request.WriteAsync(new RequeuePrintJobRequest { Id = jobId }, async () =>
                    new Requeued((await request.Service<ICommandHandler<RequeuePrintJobCommand, PrintJob>>()
                        .HandleAsync(new RequeuePrintJobCommand(jobId), ct)).Id), ct)).Id, ct),
            "print-jobs:requeue"),
        McpTools.Write("pause_print_job", "Pause print",
            "Pauses a printing job on its printer at once. " + Confirm,
            destructive: true, idempotent: false, openWorld: true,
            async (RequestContext<CallToolRequestParams> request, [Description("Printing job")] Guid jobId, CancellationToken ct = default) =>
                await request.WriteAsync(new PausePrintJobRequest { Id = jobId }, async () =>
                {
                    var job = await request.Service<ICommandHandler<PausePrintJobCommand, PrintJob>>()
                        .HandleAsync(new PausePrintJobCommand(jobId), ct);
                    var confirmed = job.Status == PrintJobStatus.Paused;
                    return new PrinterCommandResult(await DetailAsync(request, job.Id, ct), confirmed, confirmed ? null : Unconfirmed);
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
                    return new PrinterCommandResult(await DetailAsync(request, job.Id, ct), confirmed, confirmed ? null : Unconfirmed);
                }, ct);
            },
            "print-jobs:cancel"),
    ];

    /// <summary>As REST, the requeue activity row names the replacement job.</summary>
    private sealed record Requeued(Guid Id);

    public static async Task<IReadOnlyDictionary<Guid, string>> PrinterNamesAsync(RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
        (await request.Service<IPrinterRepository>().ListAsync(ct)).ToDictionary(p => p.Id, p => p.Name);

    public static McpJob Job(QueueJobRow row, IReadOnlyDictionary<Guid, string> printers)
    {
        var printerId = row.DispatchedPrinterId ?? row.PinnedPrinterId;
        return new McpJob(row.JobId, row.PartName, row.Status.ToString(), row.Phase?.ToString(), printerId,
            printerId is { } id ? printers.GetValueOrDefault(id) : null, row.DispatchedPrinterId is null && row.PinnedPrinterId is not null,
            row.ProductionRunId, row.ProductionRunId is null ? null : row.RunDisplayLabel, row.PlateQuantity, row.TargetUnits, row.QueuePosition,
            row.LastBlockReason ?? row.BlockingReason, row.StartedAtUtc, row.EndedAtUtc, row.CanRequeue);
    }

    public static async Task<McpJobDetail> DetailAsync(RequestContext<CallToolRequestParams> request, Guid jobId, CancellationToken ct, bool waiting = false)
    {
        var job = McpTools.Found(await request.Service<IQueryHandler<GetPrintJobQuery, PrintJobDetail?>>()
            .HandleAsync(new GetPrintJobQuery(jobId), ct), "Print job", "Use list_print_jobs to find job ids.");
        var phase = PrintJobResponse.FromDetail(job).Phase;
        var printers = await PrinterNamesAsync(request, ct);
        var printerId = job.DispatchedPrinterId ?? job.PinnedPrinterId;
        McpWaiting? why = null;
        if (waiting && job.Status == PrintJobStatus.Queued && job.DispatchedPrinterId is null
            && await request.Service<IPrintJobRepository>().GetByIdAsync(job.Id, ct) is { } entity)
        {
            var diagnostics = await request.Service<IPrintJobEligibilityQuery>().GetDiagnosticsAsync(entity, ct);
            why = new McpWaiting(diagnostics.Summary, diagnostics.Printers.Select(p => new McpWaitingPrinter(p.PrinterName, p.Eligible,
                p.Checks.Where(c => c.Status != "Passed").Select(c => $"{c.Label}: {c.Message ?? $"needs {c.Required}, has {c.Actual}"}").ToList(),
                p.SchedulingNote)).ToList());
        }
        return new McpJobDetail(job.Id, job.Name, job.Status.ToString(), phase, printerId,
            printerId is { } id ? printers.GetValueOrDefault(id) : null, job.DispatchedPrinterId is null && job.PinnedPrinterId is not null,
            job.PlateQuantity, job.TargetUnits, job.ProducedUnits, job.QueuePosition, job.LastBlockReason, job.CreatedAtUtc,
            job.StartedAtUtc, job.EndedAtUtc, job.ActualDurationMinutes, job.RequeuedAsJobId,
            job.FilamentConsumption.Select(f => new McpJobFilament(f.ToolIndex, f.MaterialType, f.DetectedColorHex, f.EstimatedGrams, f.GramsConsumed, f.SpoolId)).ToList(),
            why);
    }
}
