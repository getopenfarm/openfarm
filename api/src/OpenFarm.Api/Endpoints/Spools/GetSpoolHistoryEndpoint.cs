using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Spools;

public sealed class GetSpoolHistoryRequest
{
    public Guid Id { get; set; }

    /// <summary>1-based; defaults to 1.</summary>
    public int? Page { get; set; }

    /// <summary>1–100; defaults to 50, and an out-of-range value falls back to it.</summary>
    public int? PageSize { get; set; }
}

public sealed class SpoolHistoryEntryResponse
{
    public Guid Id { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; }

    /// <summary>Change to the remaining weight in grams: negative when filament was used or written off, positive when a correction or a restore added some.</summary>
    public decimal ChangeGrams { get; set; }

    /// <summary>Null on entries recorded before the history kept it.</summary>
    public decimal? RemainingAfterGrams { get; set; }

    /// <summary><c>Job</c> (a print's deduction), <c>Manual</c> (a usage entry), <c>Correction</c> (a weight edit), <c>WriteOff</c> (what was left when the spool was finished) or <c>Restore</c> (a finished spool brought back).</summary>
    public string Source { get; set; } = string.Empty;

    public string? Reason { get; set; }

    /// <summary>The print a job entry came from; null for other entries, and when the print is gone.</summary>
    public Guid? PrintId { get; set; }

    public string? PrintName { get; set; }

    public string? PrintStatus { get; set; }

    public Guid SpoolId { get; set; }

    public string? SpoolLabel { get; set; }

    public string? Material { get; set; }

    public string? ColorName { get; set; }

    public string? ColorHex { get; set; }

    public static SpoolHistoryEntryResponse FromEntry(SpoolHistoryEntry entry) => new()
    {
        Id = entry.Id,
        RecordedAtUtc = entry.RecordedAtUtc,
        ChangeGrams = -entry.GramsConsumed,
        RemainingAfterGrams = entry.RemainingAfterGrams,
        Source = entry.Source.ToString(),
        Reason = entry.Reason,
        PrintId = entry.PrintId,
        PrintName = entry.PrintName,
        PrintStatus = entry.PrintStatus?.ToString(),
        SpoolId = entry.SpoolId,
        SpoolLabel = entry.SpoolLabel,
        Material = entry.Material,
        ColorName = entry.ColorName,
        ColorHex = entry.ColorHex,
    };
}

public sealed class SpoolHistoryResponse
{
    public IReadOnlyList<SpoolHistoryEntryResponse> Entries { get; set; } = [];

    /// <summary>Entries across all pages.</summary>
    public int TotalCount { get; set; }
}

/// <summary>GET /api/v1/spools/{id}/history — every change to the spool's remaining weight, newest first. 404 for an unknown id.</summary>
[Permission("spools:list")]
public sealed class GetSpoolHistoryEndpoint(IQueryHandler<GetSpoolHistoryQuery, SpoolHistoryPage> handler)
    : Endpoint<GetSpoolHistoryRequest, SpoolHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/v1/spools/{id}/history");
        Policies("Permission");
    }

    public override async Task HandleAsync(GetSpoolHistoryRequest req, CancellationToken ct)
    {
        var pageSize = req.PageSize is >= 1 and <= GetSpoolHistoryQuery.MaxPageSize ? req.PageSize.Value : GetSpoolHistoryQuery.DefaultPageSize;
        var result = await handler.HandleAsync(new GetSpoolHistoryQuery(req.Id, Math.Max(req.Page ?? 1, 1), pageSize), ct);

        Response = new SpoolHistoryResponse
        {
            Entries = result.Entries.Select(SpoolHistoryEntryResponse.FromEntry).ToList(),
            TotalCount = result.TotalCount,
        };
    }
}
