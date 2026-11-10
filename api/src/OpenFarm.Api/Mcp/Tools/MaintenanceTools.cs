using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.Maintenance;
using OpenFarm.Application.Common;
using OpenFarm.Application.Maintenance;

namespace OpenFarm.Api.Mcp.Tools;

public static class MaintenanceTools
{
    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("list_maintenance", "List maintenance",
            "Each printer's maintenance tasks with usage since last done and due state, most due first.",
            async (RequestContext<CallToolRequestParams> request,
                [Description("Printer id")] Guid? printerId = null,
                [Description("Maintenance task id")] Guid? taskId = null,
                [Description("attention (due soon or overdue), Ok, DueSoon or Overdue")] string? state = null,
                CancellationToken ct = default) =>
                MaintenanceEnrollmentsResponse.From(await request.Service<IQueryHandler<ListMaintenanceEnrollmentsQuery, IReadOnlyList<MaintenanceEnrollmentView>>>()
                    .HandleAsync(new ListMaintenanceEnrollmentsQuery(printerId, taskId, state), ct)),
            "maintenance:view"),
    ];
}
