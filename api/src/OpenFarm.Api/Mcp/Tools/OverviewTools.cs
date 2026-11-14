using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Common;
using OpenFarm.Application.Maintenance;
using OpenFarm.Application.Production;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.Mcp.Tools;

/// <param name="NeedAction">Printers awaiting clear, offline or with alerts.</param>
public sealed record McpOverviewPrinters(int Idle, int Printing, int AwaitingClear, int Offline, IReadOnlyList<McpPrinter> NeedAction, IReadOnlyList<McpPrinter> PrintingNow);

/// <param name="Waiting">Queued jobs a printer matched but could not be sent, with the reason.</param>
/// <param name="Failed">Failed jobs that can still be requeued.</param>
public sealed record McpOverviewJobs(IReadOnlyDictionary<string, int> Counts, IReadOnlyList<McpJob> Waiting, IReadOnlyList<McpJob> Failed);

public sealed record McpOverviewRun(Guid ProductionRunId, string Name, string Status, DateOnly? DueDate, bool Late, decimal TargetUnits, decimal CompletedUnits, int BlockedJobs);

/// <param name="ShortGrams">Grams of queued work no spool of this material and colour on hand can cover.</param>
public sealed record McpOverviewFilament(Guid FilamentVariantId, string Name, decimal OnHandGrams, decimal ShortGrams, bool LowStock, DateOnly? RunsOutOn);

public sealed record McpOverviewMaintenance(Guid EnrollmentId, string Task, string Printer, string State, string DrivingTrigger, decimal Remaining);

/// <param name="Account">The person this connection acts for.</param>
/// <param name="Connection">How it signed in: <c>Token: name</c> for an access token, <c>App: name</c> for an app.</param>
/// <param name="Currency">The farm currency every price and cost is in.</param>
/// <param name="FilamentNotDeducted">Prints whose filament was not taken off a spool, newest first: a person should assign the spool used.</param>
/// <param name="Hidden">Sections left out because this connection lacks their permission.</param>
public sealed record McpOverview(string Account, string Connection, string Currency, DateTimeOffset NowUtc, McpOverviewPrinters? Printers, McpOverviewJobs? Queue,
    IReadOnlyList<McpOverviewRun>? Runs, IReadOnlyList<McpOverviewFilament>? Filament, IReadOnlyList<string>? FilamentNotDeducted,
    IReadOnlyList<McpOverviewMaintenance>? Maintenance, IReadOnlyList<string> Hidden);

public static class OverviewTools
{
    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("openfarm_get_farm_overview", "Get farm overview",
            "What needs attention now, in one call: printers awaiting clear, offline or alerting; what is printing; queued jobs that could not be sent "
            + "and why; failed jobs that can be requeued; open runs and whether they are late; low or short filament; prints whose filament was not deducted; "
            + "maintenance due soon or overdue. Also the account and the farm currency. Start here for a morning check or shift handover.",
            async (RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
            {
                var allowed = await request.AllowedAsync();
                var hidden = new List<string>();
                bool Has(string section, params string[] permissions)
                {
                    if (permissions.All(allowed.Contains)) return true;
                    hidden.Add(section);
                    return false;
                }
                var me = await request.Service<IQueryHandler<WhoAmIQuery, WhoAmIResult?>>().HandleAsync(new WhoAmIQuery(request.UserId()), ct);
                var currency = (await request.Service<IFarmSettingsRepository>().GetAsync(ct)).CurrencyCode;
                var today = DateOnly.FromDateTime(DateTime.UtcNow);

                McpOverviewPrinters? printers = null;
                if (Has("printers", "printers:list"))
                {
                    var fleet = (await PrinterTools.FleetAsync(request, ct)).Select(PrinterTools.Printer).ToList();
                    printers = new(fleet.Count(p => p.Status == nameof(PrinterStatus.Idle)), fleet.Count(p => p.Status == nameof(PrinterStatus.Printing)),
                        fleet.Count(p => p.Status == nameof(PrinterStatus.AwaitingClear)), fleet.Count(p => !p.Online),
                        fleet.Where(p => p.Status == nameof(PrinterStatus.AwaitingClear) || !p.Online || p.Alerts.Count > 0).ToList(),
                        fleet.Where(p => p.Status == nameof(PrinterStatus.Printing)).ToList());
                }

                McpOverviewJobs? queue = null;
                if (Has("queue", "print-jobs:view"))
                {
                    var jobs = request.Service<IQueryHandler<GetQueueJobsQuery, QueueJobsPage>>();
                    var names = await QueueTools.PrinterNamesAsync(request, ct);
                    var queued = await jobs.HandleAsync(new GetQueueJobsQuery(1, 100, Status: "Queued"), ct);
                    var failed = await jobs.HandleAsync(new GetQueueJobsQuery(1, 25, Status: "Failed"), ct);
                    queue = new(queued.StatusCounts,
                        queued.Items.Where(j => j.LastBlockReason is not null || j.IsBlocked).Take(20).Select(j => QueueTools.Job(j, names)).ToList(),
                        failed.Items.Where(j => j.CanRequeue).Take(10).Select(j => QueueTools.Job(j, names)).ToList());
                }

                List<McpOverviewRun>? runs = null;
                if (Has("runs", "production-runs:view"))
                    runs = (await request.Service<IQueryHandler<GetProductionRunsQuery, ProductionRunsPage>>()
                            .HandleAsync(new GetProductionRunsQuery(1, 25, Status: "Open"), ct)).Items
                        .Select(r => new McpOverviewRun(r.Id, r.DisplayLabel, r.Status.ToString(), r.DueDate,
                            r.DueDate < today && r.CompletedUnits < r.TargetUnits, r.TargetUnits, r.CompletedUnits, r.BlockedCount))
                        .ToList();

                List<McpOverviewFilament>? filament = null;
                if (Has("filament", "filaments:list", "spools:list"))
                    filament = (await request.Service<IQueryHandler<GetFilamentForecastQuery, IReadOnlyList<FilamentForecast>>>().HandleAsync(new GetFilamentForecastQuery(), ct))
                        .Where(f => f.Stock.IsLow || f.ShortGrams > 0)
                        .Select(f => new McpOverviewFilament(f.Stock.FilamentVariantId, FilamentTools.Name(f.Stock), f.Stock.Stock.OnHandGrams, f.ShortGrams,
                            f.Stock.IsLow, f.RunsOutOn))
                        .ToList();

                List<string>? notDeducted = null;
                if (Has("filamentNotDeducted", "print-jobs:view", "spools:list", "filaments:list"))
                    notDeducted = (await request.Service<IQueryHandler<ListNotificationsQuery, NotificationsPage>>().HandleAsync(new ListNotificationsQuery(10), ct))
                        .Items.Select(n => n.Name).ToList();

                List<McpOverviewMaintenance>? maintenance = null;
                if (Has("maintenance", "maintenance:view"))
                    maintenance = (await request.Service<IQueryHandler<ListMaintenanceEnrollmentsQuery, IReadOnlyList<MaintenanceEnrollmentView>>>()
                            .HandleAsync(new ListMaintenanceEnrollmentsQuery(null, null, "attention"), ct))
                        .Select(e => new McpOverviewMaintenance(e.Record.Enrollment.Id, e.Record.Task.Name, e.Record.PrinterName, e.Due.State.ToString(),
                            e.Due.DrivingTrigger.ToString(), Math.Round(e.Due.Remaining, 1)))
                        .ToList();

                var via = Security.CredentialClaims.Channel(request.Http().User);
                return new McpOverview(me?.DisplayName ?? me?.Username ?? "", via is null ? "" : $"{via.Kind}: {via.Label}", currency, DateTimeOffset.UtcNow, printers, queue, runs, filament, notDeducted, maintenance, hidden);
            }),
    ];
}
