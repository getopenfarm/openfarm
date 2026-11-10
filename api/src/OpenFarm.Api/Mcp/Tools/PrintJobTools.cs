using System.ComponentModel;
using FluentValidation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.Notifications;
using OpenFarm.Api.Endpoints.PrintJobs;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;

namespace OpenFarm.Api.Mcp.Tools;

public static class PrintJobTools
{
    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("list_print_jobs", "List print jobs",
            "Print jobs in dispatch order with status, printer, part, run and units, plus counts per status. " + McpTools.PagingNote,
            async (RequestContext<CallToolRequestParams> request,
                [Description("queue (default), backfill, or external for prints a printer started itself, newest first")] string? view = null,
                [Description("InProgress, Queued, Printing, Paused, Completed, Failed or Cancelled")] string? status = null,
                [Description("Matches the part name, or the print name for external")] string? search = null,
                [Description("1-based page, default 1")] int? page = null,
                [Description("1–100, default 25")] int? pageSize = null,
                CancellationToken ct = default) =>
            {
                new ListQueueJobsValidator().ValidateAndThrow(new ListQueueJobsRequest { Status = status });
                var jobs = ListQueueJobsResponse.From(await request.Service<IQueryHandler<GetQueueJobsQuery, QueueJobsPage>>().HandleAsync(new GetQueueJobsQuery(
                    McpTools.Page(page), McpTools.PageSize(pageSize),
                    BackfillOnly: string.Equals(view, "backfill", StringComparison.OrdinalIgnoreCase),
                    Status: status, Search: search,
                    ExternalOnly: string.Equals(view, "external", StringComparison.OrdinalIgnoreCase)), ct));
                foreach (var job in jobs.Items) job.ThumbnailBase64 = null;
                return jobs;
            },
            "print-jobs:view"),
        McpTools.Read("get_print_job", "Get print job",
            "One print job: status, printer, plate, filament use and timestamps.",
            async (RequestContext<CallToolRequestParams> request, [Description("Print job id")] Guid jobId, CancellationToken ct) =>
            {
                var job = PrintJobResponse.FromDetail(McpTools.Found(await request.Service<IQueryHandler<GetPrintJobQuery, PrintJobDetail?>>()
                    .HandleAsync(new GetPrintJobQuery(jobId), ct), "Print job"));
                job.ThumbnailBase64 = null;
                return job;
            },
            "print-jobs:view"),
        McpTools.Read("list_notifications", "List notifications",
            "Items that need action: the 50 newest prints whose filament was not deducted, low stock, unregistered RFID spools and RFID weight drift.",
            async (RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
                ListNotificationsResponse.From(await request.Service<IQueryHandler<ListNotificationsQuery, NotificationsPage>>()
                    .HandleAsync(new ListNotificationsQuery(), ct)),
            "print-jobs:view", "spools:list", "filaments:list"),
    ];
}
