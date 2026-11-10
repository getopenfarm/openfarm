using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.Printers;
using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;

namespace OpenFarm.Api.Mcp.Tools;

public static class PrinterTools
{
    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("list_printers", "List printers",
            "Every printer in the farm with its status, live telemetry and current job. Network addresses and serial numbers are left out.",
            async (RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
            {
                var fleet = ListFleetResponse.From(await request.Service<IQueryHandler<GetFleetQuery, IReadOnlyList<FleetPrinterSummary>>>()
                    .HandleAsync(new GetFleetQuery(), ct));
                foreach (var printer in fleet.Printers) (printer.IpAddress, printer.SerialNumber) = ("", "");
                return fleet;
            },
            "printers:list"),
        McpTools.Read("get_printer", "Get printer",
            "One printer: status, reported state, alerts, temperatures, progress, feed units and profiles. Network address and serial number are left out.",
            async (RequestContext<CallToolRequestParams> request, [Description("Printer id")] Guid printerId, CancellationToken ct) =>
            {
                var detail = PrinterDetailResponse.From(McpTools.Found(await request.Service<IQueryHandler<GetPrinterDetailQuery, PrinterDetail?>>()
                    .HandleAsync(new GetPrinterDetailQuery(printerId), ct), "Printer"));
                (detail.IpAddress, detail.SerialNumber) = ("", "");
                return detail;
            },
            "printers:get-detail"),
    ];
}
