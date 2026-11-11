using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Endpoints.ProductionRuns;
using OpenFarm.Api.ExceptionHandling;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;

namespace OpenFarm.Api.Mcp.Tools;

public sealed record McpPlanLine(
    [property: Description("Part or Assembly")] string Kind,
    [property: Description("Part id or assembly id from search_library")] Guid Id,
    [property: Description("How many, 1 or more")] int Quantity,
    [property: Description("Units (default for a part), Plates (part only) or Sets (assemblies, the default)")] string? Basis = null);

/// <param name="SurplusUnits">Extra units the last plate makes beyond the quantity.</param>
public sealed record McpPlannedPart(string Name, int Plates, int Units, decimal SurplusUnits);

public sealed record McpPlannedLine(string Kind, Guid Id, string Name, int Quantity, string Basis, int Plates, decimal? Hours, decimal? Grams,
    IReadOnlyList<McpPlannedPart> Parts);

/// <param name="Filament">The stocked colour this matches; several names when only material and colour are known.</param>
/// <param name="OnHandGrams">Grams in stock; empty when this connection may not read filament stock and the queue.</param>
/// <param name="AlreadyQueuedGrams">Grams unfinished queued work already needs of it.</param>
/// <param name="ShortGrams">Grams missing to print this plan after queued work; 0 when stock covers it.</param>
public sealed record McpPlanFilament(string Filament, string? MaterialType, string? ColorHex, decimal? Grams, decimal? OnHandGrams,
    decimal? AlreadyQueuedGrams, decimal? ShortGrams);

/// <param name="PlanToken">Pass to queue_production after the user approves this plan; it queues exactly this plan once.</param>
public sealed record McpProductionPlan(string PlanToken, DateTimeOffset ExpiresAtUtc, string Destination, string? Run, string? Printer,
    IReadOnlyList<McpPlannedLine> Lines, int Plates, decimal? Hours, decimal? Grams, IReadOnlyList<McpPlanFilament> Filament, IReadOnlyList<string> Warnings);

/// <param name="Outcome">Queued, or NotQueued with the reason; a later call with the same token retries only what was not queued.</param>
public sealed record McpQueuedLine(string Name, string Outcome, string? Reason);

public sealed record McpQueued(string Destination, Guid? ProductionRunId, string? Run, IReadOnlyList<McpQueuedLine> Lines, string Note);

public sealed record McpRunsPage(IReadOnlyList<ProductionRunSummaryResponse> Items, int Page, int PageSize, int TotalCount, bool HasMore,
    IReadOnlyDictionary<string, int> StatusCounts);

public static class ProductionTools
{
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(30);

    private sealed record PlannedLine(string Kind, Guid TargetId, string Name, int Quantity, string Basis, string PlanVersion, Guid SubmissionId);

    private sealed record PlannedProduction(Guid UserId, string Destination, string? RunName, DateOnly? DueDate, string? Notes,
        Guid? ProductionRunId, Guid? PrinterId, Guid SubmissionId, PlannedLine[] Lines);

    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("plan_production", "Plan production",
            "Previews queueing parts or assemblies: plates per part, surplus units, print hours, grams per filament colour against stock, and a planToken. "
            + "Nothing is queued. Show the user the plan, then call queue_production with the token once they approve it. "
            + "Destination queue queues each line on its own; newRun makes one production run of all lines; run adds the lines to an open run.",
            async (RequestContext<CallToolRequestParams> request,
                [Description("1–20 lines to make")] McpPlanLine[] lines,
                [Description("queue (default), newRun or run")] string destination = "queue",
                [Description("newRun: the run's name")] string? runName = null,
                [Description("newRun: due date, YYYY-MM-DD")] DateOnly? dueDate = null,
                [Description("newRun: notes")] string? notes = null,
                [Description("run: the open production run to add to")] Guid? productionRunId = null,
                [Description("Print every job on this printer only; omit to let any matching printer take them")] Guid? printerId = null,
                CancellationToken ct = default) =>
            {
                if (lines is not { Length: > 0 and <= 20 }) throw new ArgumentException("Pass 1–20 lines.");
                destination = destination switch
                {
                    _ when destination.Equals("queue", StringComparison.OrdinalIgnoreCase) => "queue",
                    _ when destination.Equals("newRun", StringComparison.OrdinalIgnoreCase) => "newRun",
                    _ when destination.Equals("run", StringComparison.OrdinalIgnoreCase) => "run",
                    _ => throw new ArgumentException("destination must be queue, newRun or run."),
                };
                if (destination == "run" && productionRunId is null) throw new ArgumentException("productionRunId is required for destination run.");
                if (destination != "run" && productionRunId is not null)
                    throw new ArgumentException("productionRunId is only for destination run; omit it, or use destination run to add to that run.");
                var allowed = await request.AllowedAsync();
                string? run = destination == "newRun" ? runName : null;
                if (productionRunId is { } runId && allowed.Contains("production-runs:view"))
                    run = McpTools.Found(await request.Service<IQueryHandler<GetProductionRunDetailQuery, RunDetailView?>>()
                        .HandleAsync(new GetProductionRunDetailQuery(runId), ct), "Production run", "Use list_production_runs to find open runs.").DisplayLabel;
                var printers = await QueueTools.PrinterNamesAsync(request, ct);
                string? printer = null;
                if (printerId is { } pid && !printers.TryGetValue(pid, out printer))
                    throw new KeyNotFoundException("Printer not found. Use list_printers to find printer ids.");
                if (!allowed.Contains("printers:list")) printer = null;

                var planner = request.Service<ProductionPlanner>();
                var planned = new List<(PlannedLine Line, ProductionPlan Plan)>();
                foreach (var line in lines)
                {
                    var kind = Enum.TryParse<RunLineTargetKind>(line.Kind, ignoreCase: true, out var k) && Enum.IsDefined(k) ? k
                        : throw new ArgumentException("kind must be Part or Assembly.");
                    var basis = line.Basis is null ? (kind == RunLineTargetKind.Part ? QuantityBasis.Units : QuantityBasis.Sets)
                        : Enum.TryParse<QuantityBasis>(line.Basis, ignoreCase: true, out var b) && Enum.IsDefined(b) ? b
                        : throw new ArgumentException("basis must be Units, Plates or Sets.");
                    var dto = LineRequest(kind.ToString(), line.Id, line.Quantity, basis.ToString(), null, printerId);
                    await new CreateRunLineRequestValidator().ValidateAndThrowAsync(dto, ct);
                    if (destination != "queue" && kind == RunLineTargetKind.Assembly && basis == QuantityBasis.Plates)
                        throw new ArgumentException("Assembly plate counts are only for destination queue; runs count assemblies in sets.");
                    var plan = await planner.PlanAsync(new ProductionPlanInput(kind, line.Id, line.Quantity, basis), ct);
                    var name = kind == RunLineTargetKind.Part
                        ? (plan.Parts.Count > 0 ? plan.Parts[0].Name : "Part")
                        : (await request.Service<IAssemblyRepository>().GetByIdAsync(line.Id, ct))?.Name ?? "Assembly";
                    planned.Add((new PlannedLine(kind.ToString(), line.Id, name, line.Quantity, basis.ToString(), plan.Version, Guid.NewGuid()), plan));
                }

                // Stock is filament and spool data: leave it out unless the connection may read both.
                var seesStock = allowed.Contains("filaments:list") && allowed.Contains("spools:list") && allowed.Contains("print-jobs:view");
                var stock = seesStock
                    ? await request.Service<IQueryHandler<GetFilamentStockQuery, IReadOnlyList<VariantStock>>>().HandleAsync(new GetFilamentStockQuery(), ct)
                    : [];
                var queued = seesStock ? await GetFilamentShortagesQueryHandler.QueuedDemandAsync(request.Service<IPrintJobRepository>(), ct) : QueuedDemand.None;
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var noPurchases = new Dictionary<Guid, SpoolPurchase>();
                // A colour's shortage after queued work, across every brand of its material and hex, as list_filaments counts it.
                decimal ShortOf(QueuedDemand demand, ColourKey key) => FilamentForecasts
                    .Compute([.. stock.Where(s => ColourKey.Of(s.MaterialType, s.ColorHex) == key)], demand, noPurchases, 0, today).Sum(f => f.ShortGrams);
                var filament = new List<McpPlanFilament>();
                foreach (var g in planned.SelectMany(p => p.Plan.Estimate?.Colors ?? []).GroupBy(c => (c.VariantId, c.MaterialType, Hex: c.ColorHex?.ToUpperInvariant())))
                {
                    var grams = g.Any(c => c.Grams is null) ? null : g.Sum(c => c.Grams);
                    var label = $"{g.Key.MaterialType ?? "Any material"} {g.Key.Hex}";
                    if (!seesStock) { filament.Add(new(label, g.Key.MaterialType, g.Key.Hex, grams, null, null, null)); continue; }
                    var matches = GetFilamentShortagesQueryHandler.Matching(stock, g.Key.VariantId, g.Key.MaterialType, g.Key.Hex).ToList();
                    if (matches.Count == 0) { filament.Add(new($"{label} (not in stock)", g.Key.MaterialType, g.Key.Hex, grams, 0, 0, grams)); continue; }
                    // The plan's grams join the queue in order, so each row's shortfall is what it adds.
                    var key = ColourKey.Of(matches[0].MaterialType, matches[0].ColorHex);
                    var already = g.Key.VariantId is { } id ? queued.ByVariant.GetValueOrDefault(id)
                        : queued.ByColour.GetValueOrDefault(key) + matches.Sum(m => queued.ByVariant.GetValueOrDefault(m.FilamentVariantId));
                    decimal? shortGrams = null;
                    if (grams is { } need)
                    {
                        var next = queued.Plus(g.Key.VariantId, key, need);
                        shortGrams = ShortOf(next, key) - ShortOf(queued, key);
                        queued = next;
                    }
                    filament.Add(new(string.Join(" / ", matches.Select(FilamentTools.Name)), g.Key.MaterialType, g.Key.Hex, grams,
                        matches.Sum(m => m.Stock.OnHandGrams), already, shortGrams));
                }

                var warnings = new List<string>();
                if (planned.Any(p => p.Plan.Estimate?.Time.Missing > 0 || p.Plan.Estimate?.Filament.Missing > 0))
                    warnings.Add("Some parts have no print estimate, so hours or grams are incomplete.");
                if (planned.Any(p => p.Plan.Estimate?.Approximate == true))
                    warnings.Add("Partial-plate estimates are proportional approximations.");
                if (filament.Any(f => f.ShortGrams > 0))
                    warnings.Add("Stock does not cover this plan after queued work; jobs wait for filament that is not on hand.");

                var token = new PlannedProduction(request.UserId(), destination, runName, dueDate, notes, productionRunId, printerId, Guid.NewGuid(),
                    [.. planned.Select(p => p.Line)]);
                var expires = DateTimeOffset.UtcNow.Add(PlanLifetime);
                return new McpProductionPlan(Protector(request).Protect(JsonSerializer.Serialize(token), expires), expires, destination, run, printer,
                    planned.Select(p => new McpPlannedLine(p.Line.Kind, p.Line.TargetId, p.Line.Name, p.Line.Quantity, p.Line.Basis, p.Plan.Plates,
                        p.Plan.Minutes / 60m, p.Plan.Grams,
                        p.Plan.Parts.Select(part => new McpPlannedPart(part.Name, part.Plates, part.PlannedUnits, part.SurplusUnits)).ToList())).ToList(),
                    planned.Sum(p => p.Plan.Plates),
                    planned.Any(p => p.Plan.Minutes is null) ? null : planned.Sum(p => p.Plan.Minutes) / 60m,
                    planned.Any(p => p.Plan.Grams is null) ? null : planned.Sum(p => p.Plan.Grams),
                    filament, warnings);
            },
            "production-runs:create"),
        McpTools.Write("queue_production", "Queue production",
            "Queues exactly the plan plan_production returned. Call it only after the user approved that plan. Idle printers can start the jobs at once. "
            + "Repeating the call with the same token never queues anything twice.",
            destructive: false, idempotent: true, openWorld: true,
            async (RequestContext<CallToolRequestParams> request,
                [Description("planToken from plan_production, valid 30 minutes")] string planToken,
                CancellationToken ct = default) =>
            {
                PlannedProduction plan;
                try { plan = JsonSerializer.Deserialize<PlannedProduction>(Protector(request).Unprotect(planToken))!; }
                catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
                {
                    throw new ArgumentException("planToken is invalid or expired. Call plan_production again and show the user the new plan.");
                }
                if (plan.UserId != request.UserId()) throw new ArgumentException("planToken belongs to another account. Call plan_production again.");

                const string note = "Idle printers may start these jobs now. Follow them with list_print_jobs or get_production_run.";
                if (plan.Destination == "newRun")
                {
                    var dto = new CreateProductionRunRequest
                    {
                        SubmissionId = plan.SubmissionId, Name = plan.RunName, DueDate = plan.DueDate, Notes = plan.Notes,
                        Lines = [.. plan.Lines.Select(l => LineRequest(l.Kind, l.TargetId, l.Quantity, l.Basis, l.PlanVersion, plan.PrinterId))],
                    };
                    var created = await request.WriteAsync(dto, async () =>
                    {
                        var run = await request.Service<ICommandHandler<CreateProductionRunCommand, ProductionRun>>().HandleAsync(new CreateProductionRunCommand(
                            dto.Name, dto.DueDate, dto.Notes, dto.Lines.Select(l => l.ToInput()).ToList(), dto.SubmissionId), ct);
                        return ProductionRunDetailResponse.FromView((await request.Service<IQueryHandler<GetProductionRunDetailQuery, RunDetailView?>>()
                            .HandleAsync(new GetProductionRunDetailQuery(run.Id), ct))!);
                    }, ct);
                    return new McpQueued(plan.Destination, created.Id, created.DisplayLabel,
                        plan.Lines.Select(l => new McpQueuedLine(l.Name, "Queued", null)).ToList(), note);
                }

                var results = new List<McpQueuedLine>();
                foreach (var line in plan.Lines)
                {
                    // Each line gets its own scope, so work a failed line had already staged cannot be saved with the next line.
                    await using var scope = request.Services!.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<ProductionDemandService>();
                    var dto = new SubmitProductionDemandRequest
                    {
                        SubmissionId = line.SubmissionId, PlanVersion = line.PlanVersion, ProductionRunId = plan.ProductionRunId,
                        Line = LineRequest(line.Kind, line.TargetId, line.Quantity, line.Basis, line.PlanVersion, plan.PrinterId),
                    };
                    try
                    {
                        await request.WriteAsync(dto, async () => ProductionDemandResponse.From(await service.SubmitAsync(
                            new(dto.SubmissionId, dto.Line.ToInput(), dto.PlanVersion, dto.ProductionRunId), ct)), ct);
                        results.Add(new(line.Name, "Queued", null));
                    }
                    catch (Exception e) when (DomainExceptionHandler.ToProblem(e) is { } problem)
                    {
                        results.Add(new(line.Name, "NotQueued", e is ProductionPlanConflictException
                            ? "The part or assembly changed since the plan. Call plan_production again and show the user the new plan."
                            : problem.Detail ?? problem.Title));
                    }
                }
                return new McpQueued(plan.Destination, plan.ProductionRunId, null, results, note);
            },
            "production-runs:create"),
        McpTools.Read("list_production_runs", "List production runs",
            "Production runs with their lines, unit progress and plate counts, plus counts per status. " + McpTools.PagingNote,
            async (RequestContext<CallToolRequestParams> request,
                [Description("Open (Active or Complete), Closed or Cancelled; omit for every run")] string? status = null,
                [Description("Matches the run name or a line's target name")] string? search = null,
                [Description(McpTools.PageArg)] int? page = null,
                [Description(McpTools.PageSizeArg)] int? pageSize = null,
                CancellationToken ct = default) =>
            {
                new ListProductionRunsValidator().ValidateAndThrow(new ListProductionRunsRequest { Status = status });
                var (p, size) = (McpTools.Page(page), McpTools.PageSize(pageSize));
                var runs = ListProductionRunsResponse.From(await request.Service<IQueryHandler<GetProductionRunsQuery, ProductionRunsPage>>()
                    .HandleAsync(new GetProductionRunsQuery(p, size, Status: status, Search: search), ct));
                return new McpRunsPage(runs.Items, p, size, runs.TotalCount, p * size < runs.TotalCount, runs.StatusCounts);
            },
            "production-runs:view"),
        McpTools.Read("get_production_run", "Get production run",
            "One production run: its lines with their part requirements and jobs.",
            async (RequestContext<CallToolRequestParams> request, [Description("Production run id")] Guid productionRunId, CancellationToken ct) =>
                ProductionRunDetailResponse.FromView(McpTools.Found(await request.Service<IQueryHandler<GetProductionRunDetailQuery, RunDetailView?>>()
                    .HandleAsync(new GetProductionRunDetailQuery(productionRunId), ct), "Production run", "Use list_production_runs to find run ids.")),
            "production-runs:view"),
    ];

    private static ITimeLimitedDataProtector Protector(RequestContext<CallToolRequestParams> request) =>
        request.Service<IDataProtectionProvider>().CreateProtector("OpenFarm.Mcp.ProductionPlan.v1").ToTimeLimitedDataProtector();

    private static CreateRunLineRequest LineRequest(string kind, Guid id, int quantity, string basis, string? planVersion, Guid? printerId) => new()
    {
        TargetKind = kind,
        TargetId = id,
        Quantity = quantity,
        QuantityBasis = basis,
        PlanVersion = planVersion,
        AssignmentMode = printerId is null ? "Unassigned" : "Pinned",
        PinnedPrinterId = printerId,
    };
}
