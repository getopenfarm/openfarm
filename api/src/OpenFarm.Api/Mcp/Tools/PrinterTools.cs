using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.Mcp.Tools;

/// <param name="Status">Idle, Printing or AwaitingClear (a print ended and the plate waits for a person to clear it).</param>
/// <param name="AwaitingClearSinceUtc">Set while awaiting clear; pass it to openfarm_mark_printers_ready.</param>
/// <param name="CurrentPrint">The print on the printer: the one printing, or the one whose plate waits to be cleared.</param>
/// <param name="Alerts">Active printer alerts, such as a filament runout or an error code.</param>
public sealed record McpPrinter(Guid PrinterId, string Name, string Model, string Status, bool Online, DateTimeOffset? AwaitingClearSinceUtc,
    string? CurrentPrint, Guid? CurrentJobId, int? ProgressPercent, int? RemainingMinutes, IReadOnlyList<string> Alerts);

public sealed record McpPrinters(IReadOnlyList<McpPrinter> Printers);

public sealed record McpReadyPrinter(
    [property: Description("Printer id from openfarm_list_printers")] Guid PrinterId,
    [property: Description("awaitingClearSinceUtc as openfarm_list_printers returned it")] DateTimeOffset AwaitingClearSinceUtc);

/// <param name="Outcome">Ready; NotAwaitingClear (nothing to do); ChangedSinceRead (another print ended since you read it: ask the user about that plate); NotFound.</param>
public sealed record McpReadyResult(Guid PrinterId, string? Name, string Outcome);

public sealed record McpReadyResults(IReadOnlyList<McpReadyResult> Printers);

public static class PrinterTools
{
    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("openfarm_list_printers", "List printers",
            "Every printer with its status, whether it is online, the print on it with progress and time left, and active alerts. "
            + "Printers awaiting clear carry awaitingClearSinceUtc for openfarm_mark_printers_ready.",
            async (RequestContext<CallToolRequestParams> request,
                [Description("Only printers with this status: Idle, Printing or AwaitingClear; or Offline for printers not reachable now")] string? status = null,
                CancellationToken ct = default) =>
            {
                var printers = (await FleetAsync(request, ct)).Select(Printer);
                return new McpPrinters(status is null ? printers.ToList()
                    : string.Equals(status, "Offline", StringComparison.OrdinalIgnoreCase) ? printers.Where(p => !p.Online).ToList()
                    : Enum.TryParse<PrinterStatus>(status, ignoreCase: true, out var s) && Enum.IsDefined(s)
                        ? printers.Where(p => p.Status == s.ToString()).ToList()
                        : throw new ArgumentException("status must be Idle, Printing, AwaitingClear or Offline."));
            },
            "printers:list"),
        McpTools.Read("openfarm_get_printer", "Get printer",
            "One printer in detail: reported state, alerts, temperatures, progress, feed units with the filament loaded in each slot, and profiles. "
            + "Network address and serial number are left out.",
            async (RequestContext<CallToolRequestParams> request, [Description("Printer id")] Guid printerId, CancellationToken ct) =>
            {
                var detail = PrinterDetailResponse.From(McpTools.Found(await request.Service<IQueryHandler<GetPrinterDetailQuery, PrinterDetail?>>()
                    .HandleAsync(new GetPrinterDetailQuery(printerId), ct), "Printer", "Use openfarm_list_printers to find printer ids."));
                (detail.IpAddress, detail.SerialNumber) = ("", "");
                return detail;
            },
            "printers:get-detail"),
        McpTools.Write("openfarm_mark_printers_ready", "Mark printers ready",
            "Marks printers awaiting clear as ready, so each can take its next queued job at once. Call it only for printers the user says in this "
            + "conversation have a clear build plate; never because a print finished. Each printer is handled on its own.",
            destructive: false, idempotent: true, openWorld: true,
            async (RequestContext<CallToolRequestParams> request,
                [Description("Printers with a clear plate, each with awaitingClearSinceUtc from openfarm_list_printers")] McpReadyPrinter[] printers,
                CancellationToken ct = default) =>
            {
                if (printers is not { Length: > 0 and <= 100 }) throw new ArgumentException("Pass 1–100 printers.");
                var handler = request.Service<ICommandHandler<MarkPrinterReadyCommand, Printer>>();
                var results = new List<McpReadyResult>();
                foreach (var target in printers)
                {
                    try
                    {
                        var printer = await request.WriteAsync(new MarkPrinterReadyRequest { Id = target.PrinterId }, async () =>
                            PrinterSummaryResponse.FromDomain(await handler.HandleAsync(new MarkPrinterReadyCommand(target.PrinterId, target.AwaitingClearSinceUtc), ct)), ct);
                        results.Add(new(target.PrinterId, printer.Name, "Ready"));
                    }
                    catch (InvalidPrinterStatusTransitionException) { results.Add(new(target.PrinterId, null, "NotAwaitingClear")); }
                    catch (PrinterChangedSinceReadException) { results.Add(new(target.PrinterId, null, "ChangedSinceRead")); }
                    catch (PrinterNotFoundException) { results.Add(new(target.PrinterId, null, "NotFound")); }
                }
                return new McpReadyResults(results);
            },
            "printers:mark-ready"),
    ];

    public static async Task<IReadOnlyList<FleetPrinterSummary>> FleetAsync(RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
        await request.Service<IQueryHandler<GetFleetQuery, IReadOnlyList<FleetPrinterSummary>>>().HandleAsync(new GetFleetQuery(), ct);

    public static McpPrinter Printer(FleetPrinterSummary p) => new(p.Id, p.Name, $"{p.Brand} {p.ModelCode}", p.Status.ToString(), p.IsReachable,
        p.Status == PrinterStatus.AwaitingClear ? p.StatusSinceUtc : null,
        p.CurrentJobName, p.CurrentPrintJobId,
        p.Status == PrinterStatus.Printing ? p.PrintProgressPercent : null, p.Status == PrinterStatus.Printing ? p.RemainingTimeMinutes : null,
        (p.Alerts ?? []).Select(a => $"{a.Severity}: {a.Message}").ToList());
}
