using System.Text.Json;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Api.Security;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.Endpoints.ActivityLog;

public sealed class AuditLogRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public Guid? ActorId { get; set; }
    public string? Channel { get; set; }
    public Guid? CredentialId { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Action { get; set; }
    public DateTimeOffset? From { get; set; }
    /// <summary>Exclusive end instant, in UTC.</summary>
    public DateTimeOffset? To { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed record AuditEntryResponse(Guid Id, string EntityType, string EntityId, string? EntityLabel,
    string EventType, string? Action, Guid? ActorId, string? ActorName, string? Channel,
    Guid? CredentialId, string? CredentialLabel, string? Operation, string? CorrelationId,
    DateTimeOffset TimestampUtc, IReadOnlyList<AuditFieldChange> Changes, bool Historical, ActivityLogDetails? Details = null, string? Href = null);
public sealed record AuditLogPageResponse(IReadOnlyList<AuditEntryResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record AuditActorOption(Guid Id, string Name);
public sealed record AuditCredentialOption(Guid Id, string Label, string Channel);
public sealed record AuditFilterOptionsResponse(IReadOnlyList<AuditActorOption> Actors,
    IReadOnlyList<AuditCredentialOption> Credentials, IReadOnlyList<string> EntityTypes, IReadOnlyList<string> Actions);
public sealed class AuditEntryRequest { public Guid Id { get; set; } }

internal static class AuditRead
{
    public static IQueryable<ActivityLogEntry> Query(OpenFarmDbContext db) =>
        db.ActivityLogEntries.AsNoTracking().Where(e => e.Category == "Audit");

    public static AuditEntryResponse Entry(ActivityLogEntry e) => new(e.Id, e.EntityType, e.EntityId, e.EntityLabel,
        e.EventType, e.Action, e.ActorId, e.ActorName, e.Channel, e.CredentialId, e.CredentialLabel,
        e.Operation, e.CorrelationId, e.TimestampUtc,
        e.ChangesJson is null ? [] : JsonSerializer.Deserialize<List<AuditFieldChange>>(e.ChangesJson)!, e.Action is null, e.Details);

    public static async Task<AuditLogPageResponse> Page(IQueryable<ActivityLogEntry> query, int page, int size, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 100_000);
        size = size is < 1 or > 100 ? 25 : size;
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.TimestampUtc).ThenBy(e => e.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new(items.Select(Entry).ToList(), page, size, total);
    }
}

[Permission("audit-log:list")]
public sealed class GetAuditLogEndpoint(OpenFarmDbContext db) : Endpoint<AuditLogRequest, AuditLogPageResponse>
{
    public override void Configure() { Get("/api/v1/audit-log"); Policies("Permission"); }
    public override async Task HandleAsync(AuditLogRequest req, CancellationToken ct)
    {
        if (req.From > req.To) { AddError("From must be before To."); await Send.ErrorsAsync(cancellation: ct); return; }
        var query = AuditRead.Query(db);
        if (req.ActorId is { } actor) query = query.Where(e => e.ActorId == actor);
        if (req.Channel == "Agents") query = query.Where(e => e.Channel == "Token" || e.Channel == "App");
        else if (!string.IsNullOrWhiteSpace(req.Channel)) query = query.Where(e => e.Channel == req.Channel);
        if (req.CredentialId is { } credential) query = query.Where(e => e.CredentialId == credential);
        if (req.EntityType == "Printer" && Guid.TryParse(req.EntityId, out var printerId))
            query = query.Where(e => e.EntityType == "Printer" && e.EntityId == req.EntityId || e.RelatedPrinterIds.Contains(printerId));
        else
        {
            if (!string.IsNullOrWhiteSpace(req.EntityType)) query = query.Where(e => e.EntityType == req.EntityType);
            if (!string.IsNullOrWhiteSpace(req.EntityId)) query = query.Where(e => e.EntityId == req.EntityId);
        }
        if (!string.IsNullOrWhiteSpace(req.Action)) query = query.Where(e => e.Action == req.Action);
        if (req.From is { } from) query = query.Where(e => e.TimestampUtc >= from);
        if (req.To is { } to) query = query.Where(e => e.TimestampUtc < to);
        if (!string.IsNullOrWhiteSpace(req.CorrelationId)) query = query.Where(e => e.CorrelationId == req.CorrelationId);
        Response = await AuditRead.Page(query, req.Page, req.PageSize, ct);
    }
}

[Permission("audit-log:list")]
public sealed class GetAuditEntryEndpoint(OpenFarmDbContext db) : Endpoint<AuditEntryRequest, AuditEntryResponse>
{
    public override void Configure() { Get("/api/v1/audit-log/{id:guid}"); Policies("Permission"); }
    public override async Task HandleAsync(AuditEntryRequest req, CancellationToken ct)
    {
        var entry = await AuditRead.Query(db).SingleOrDefaultAsync(e => e.Id == req.Id, ct);
        if (entry is null) { await Send.NotFoundAsync(ct); return; }
        var response = AuditRead.Entry(entry);
        var target = entry.EntityType switch
        {
            "Printer" => ("printers:get-detail", "printers", "Printer"),
            "Part" => ("parts:read", "parts", "Part"),
            "Assembly" => ("assemblies:view", "assemblies", "Assembly"),
            "Spool" => ("spools:list", "filament/spools", "Spool"),
            "Filament" => ("filaments:list", "filament", "Filament"),
            "PrintJob" => ("print-jobs:view", "jobs", "PrintJob"),
            "ProductionRun" => ("production-runs:view", "production-runs", "ProductionRun"),
            "MachineProfile" => ("profiles:get-machine", "profiles/machine", "MachineProfile"),
            "FilamentProfile" => ("profiles:get-filament", "profiles/filament", "FilamentProfile"),
            "ProcessProfile" => ("profiles:get-process", "profiles/process", "ProcessProfile"),
            "MaintenanceTask" => ("maintenance:view", "maintenance", "MaintenanceTask"),
            _ => ((string Permission, string Route, string Type)?)null,
        };
        if (target is { } link && Guid.TryParse(entry.EntityId, out var entityId)
            && (await CredentialAccess.EffectiveAsync(HttpContext, browser: true)).Contains(link.Permission))
        {
            var entityType = db.Model.GetEntityTypes().FirstOrDefault(t => t.ClrType.Name == link.Type)?.ClrType;
            if (entityType is not null && await db.FindAsync(entityType, [entityId], ct) is not null)
                response = response with { Href = $"/{link.Route}/{entityId}" };
        }
        Response = response;
    }
}

[Permission("audit-log:list")]
public sealed class GetRelatedAuditEntriesEndpoint(OpenFarmDbContext db) : Endpoint<AuditEntryRequest, AuditLogPageResponse>
{
    public override void Configure() { Get("/api/v1/audit-log/{id:guid}/related"); Policies("Permission"); }
    public override async Task HandleAsync(AuditEntryRequest req, CancellationToken ct)
    {
        var entry = await AuditRead.Query(db).SingleOrDefaultAsync(e => e.Id == req.Id, ct);
        if (entry is null) { await Send.NotFoundAsync(ct); return; }
        Response = entry.CorrelationId is null ? new([], 1, 25, 0)
            : await AuditRead.Page(AuditRead.Query(db).Where(e => e.CorrelationId == entry.CorrelationId && e.Id != entry.Id), 1, 25, ct);
    }
}

[Permission("audit-log:list")]
public sealed class GetAuditFilterOptionsEndpoint(OpenFarmDbContext db) : EndpointWithoutRequest<AuditFilterOptionsResponse>
{
    public override void Configure() { Get("/api/v1/audit-log/filter-options"); Policies("Permission"); }
    public override async Task HandleAsync(CancellationToken ct)
    {
        var query = AuditRead.Query(db);
        var actors = await query.Where(e => e.ActorId != null).GroupBy(e => e.ActorId)
            .Select(g => g.OrderByDescending(e => e.TimestampUtc).ThenBy(e => e.Id)
                .Select(e => new AuditActorOption(e.ActorId!.Value, e.ActorName ?? "Unknown historical user")).First())
            .ToListAsync(ct);
        var credentials = await query.Where(e => e.CredentialId != null).GroupBy(e => e.CredentialId)
            .Select(g => g.OrderByDescending(e => e.TimestampUtc).ThenBy(e => e.Id)
                .Select(e => new AuditCredentialOption(e.CredentialId!.Value, e.CredentialLabel ?? "Unknown credential", e.Channel ?? "Token")).First())
            .ToListAsync(ct);
        Response = new(actors.OrderBy(a => a.Name, StringComparer.Ordinal).ToList(), credentials,
            await query.Select(e => e.EntityType).Distinct().OrderBy(e => e).ToListAsync(ct),
            await query.Where(e => e.Action != null).Select(e => e.Action!).Distinct().OrderBy(e => e).ToListAsync(ct));
    }
}
