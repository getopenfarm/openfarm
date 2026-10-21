using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Slicing;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.Endpoints.Slicing;

public sealed class GetSlicingReadinessRequest
{
    public string? Engine { get; set; }

    public string? Version { get; set; }
}

public sealed class ReadinessIssueResponse
{
    /// <summary>Machine, Process, Filament or Plate; absent for a problem of the whole selection.</summary>
    public string? Kind { get; set; }

    /// <summary>A resolution outcome (Missing, Ambiguous, NotOffered, UnsupportedOrigin, CatalogNotLoaded, Incompatible, ...) or NotSet.</summary>
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>The page that fixes it.</summary>
    public string Link { get; set; } = string.Empty;

    public static ReadinessIssueResponse From(ReadinessIssue issue) => new() { Kind = issue.Kind, Code = issue.Code, Message = issue.Message, Link = issue.Link };
}

public sealed class PrinterReadinessResponse
{
    public Guid PrinterId { get; set; }

    public string PrinterName { get; set; } = string.Empty;

    public List<ReadinessIssueResponse> Issues { get; set; } = [];
}

public sealed class FilamentReadinessResponse
{
    public Guid FilamentId { get; set; }

    public string FilamentName { get; set; } = string.Empty;

    public string Brand { get; set; } = string.Empty;

    public string ModelCode { get; set; } = string.Empty;

    public decimal NozzleDiameterMm { get; set; }

    public string? ColorHex { get; set; }

    public string ProfileName { get; set; } = string.Empty;

    /// <summary>The printers this assignment applies to (same brand, model and nozzle).</summary>
    public List<Guid> PrinterIds { get; set; } = [];

    public List<ReadinessIssueResponse> Issues { get; set; } = [];
}

public sealed class SlicingReadinessResponse
{
    public string Engine { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>True when every printer's presets and every filament assignment resolve to this slicer's own catalog.</summary>
    public bool IsReady { get; set; }

    public List<PrinterReadinessResponse> Printers { get; set; } = [];

    public List<FilamentReadinessResponse> Filaments { get; set; } = [];

    public static SlicingReadinessResponse From(SlicingReadinessReport report) => new()
    {
        Engine = report.Pin.Engine.ToString(),
        Version = report.Pin.Version,
        IsReady = report.IsReady,
        Printers = report.Printers.Select(p => new PrinterReadinessResponse
        {
            PrinterId = p.PrinterId,
            PrinterName = p.PrinterName,
            Issues = p.Issues.Select(ReadinessIssueResponse.From).ToList(),
        }).ToList(),
        Filaments = report.Filaments.Select(f => new FilamentReadinessResponse
        {
            FilamentId = f.FilamentId,
            FilamentName = f.FilamentName,
            Brand = f.Brand,
            ModelCode = f.ModelCode,
            NozzleDiameterMm = f.NozzleDiameterMm,
            ColorHex = f.ColorHex,
            ProfileName = f.ProfileName,
            PrinterIds = f.PrinterIds.ToList(),
            Issues = f.Issues.Select(ReadinessIssueResponse.From).ToList(),
        }).ToList(),
    };
}

/// <summary>GET /api/v1/slicing/readiness?engine=&amp;version= — what a part pinned to that exact slicer could not be sliced with on this farm.</summary>
[Permission("settings:slicing-defaults:read")]
public sealed class GetSlicingReadinessEndpoint(IQueryHandler<GetSlicingReadinessQuery, SlicingReadinessReport> handler)
    : Endpoint<GetSlicingReadinessRequest, SlicingReadinessResponse>
{
    public override void Configure()
    {
        Get("/api/v1/slicing/readiness");
        Policies("Permission");
    }

    public override async Task HandleAsync(GetSlicingReadinessRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<SlicerEngine>(req.Engine, ignoreCase: true, out var engine))
        {
            ThrowError("Engine must be OrcaSlicer, BambuStudio, or SuperSlicer.");
            return;
        }

        if (string.IsNullOrWhiteSpace(req.Version))
        {
            ThrowError("Version is required.");
            return;
        }

        Response = SlicingReadinessResponse.From(await handler.HandleAsync(new GetSlicingReadinessQuery(engine, req.Version), ct));
    }
}
