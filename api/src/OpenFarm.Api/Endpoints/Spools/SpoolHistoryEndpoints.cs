using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Spools;

public class SpoolHistoryFilterRequest
{
    /// <summary>Entries recorded at or after this instant.</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Entries recorded before this instant.</summary>
    public DateTimeOffset? To { get; set; }

    /// <summary>Only spools of this color.</summary>
    public Guid? FilamentVariantId { get; set; }

    /// <summary>Only spools of this filament's colors.</summary>
    public Guid? FilamentId { get; set; }

    /// <summary><c>Job</c>, <c>Manual</c>, <c>Correction</c>, <c>WriteOff</c> or <c>Restore</c>.</summary>
    public string? Source { get; set; }

    internal GetSpoolHistoryQuery ToQuery(int page, int pageSize) =>
        new(null, page, pageSize, From?.ToUniversalTime(), To?.ToUniversalTime(), FilamentVariantId,
            Source is null ? null : Enum.Parse<SpoolUsageSource>(Source, ignoreCase: true), FilamentId);
}

public sealed class SpoolHistoryFilterValidator : Validator<SpoolHistoryFilterRequest>
{
    public SpoolHistoryFilterValidator() =>
        RuleFor(x => x.Source)
            .Must(source => source is null || Enum.GetNames<SpoolUsageSource>().Contains(source, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Source must be Job, Manual, Correction, WriteOff or Restore.");
}

public sealed class ListSpoolHistoryRequest : SpoolHistoryFilterRequest
{
    /// <summary>1-based; defaults to 1.</summary>
    public int? Page { get; set; }

    /// <summary>1–100; defaults to 50, and an out-of-range value falls back to it.</summary>
    public int? PageSize { get; set; }
}

public sealed class ListSpoolHistoryValidator : Validator<ListSpoolHistoryRequest>
{
    public ListSpoolHistoryValidator() => Include(new SpoolHistoryFilterValidator());
}

/// <summary>GET /api/v1/spools/history — every spool's weight changes, newest first, filtered by time, filament, color and source.</summary>
[Permission("spools:list")]
public sealed class ListSpoolHistoryEndpoint(IQueryHandler<GetSpoolHistoryQuery, SpoolHistoryPage> handler)
    : Endpoint<ListSpoolHistoryRequest, SpoolHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/v1/spools/history");
        Policies("Permission");
    }

    public override async Task HandleAsync(ListSpoolHistoryRequest req, CancellationToken ct)
    {
        var pageSize = req.PageSize is >= 1 and <= GetSpoolHistoryQuery.MaxPageSize ? req.PageSize.Value : GetSpoolHistoryQuery.DefaultPageSize;
        var result = await handler.HandleAsync(req.ToQuery(Math.Max(req.Page ?? 1, 1), pageSize), ct);

        Response = new SpoolHistoryResponse
        {
            Entries = result.Entries.Select(SpoolHistoryEntryResponse.FromEntry).ToList(),
            TotalCount = result.TotalCount,
        };
    }
}

/// <summary>GET /api/v1/spools/history.csv — the same entries as a CSV file, all of them.</summary>
[Permission("spools:list")]
public sealed class ExportSpoolHistoryEndpoint(IQueryHandler<GetSpoolHistoryQuery, SpoolHistoryPage> handler)
    : Endpoint<SpoolHistoryFilterRequest>
{
    public override void Configure()
    {
        Get("/api/v1/spools/history.csv");
        Policies("Permission");
    }

    public override async Task HandleAsync(SpoolHistoryFilterRequest req, CancellationToken ct)
    {
        // ponytail: the whole export is built in memory; stream rows if exports pass ~100k entries.
        var result = await handler.HandleAsync(req.ToQuery(1, int.MaxValue), ct);
        var csv = Csv.Write(
            ["Recorded (UTC)", "Spool", "Spool id", "Material", "Color", "Change (g)", "Remaining (g)", "Source", "Reason", "Print", "Print status"],
            result.Entries.Select(e => new object?[]
            {
                e.RecordedAtUtc, e.SpoolLabel, e.SpoolId, e.Material, e.ColorName, -e.GramsConsumed, e.RemainingAfterGrams, e.Source,
                e.Reason, e.PrintName, e.PrintStatus,
            }));
        await Send.BytesAsync(csv, "spool-history.csv", "text/csv", cancellation: ct);
    }
}
