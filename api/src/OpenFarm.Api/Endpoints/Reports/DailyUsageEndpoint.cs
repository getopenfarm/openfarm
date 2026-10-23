using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Api.Endpoints.Reports;

public sealed class DailyUsageRequest
{
    public Guid FilamentId { get; set; }

    /// <summary>The IANA time zone the days are counted in, such as <c>Asia/Kuala_Lumpur</c>.</summary>
    public string TimeZone { get; set; } = string.Empty;
}

public sealed class DailyUsageRequestValidator : Validator<DailyUsageRequest>
{
    public DailyUsageRequestValidator()
    {
        RuleFor(x => x.FilamentId).NotEmpty();
        RuleFor(x => x.TimeZone)
            .NotEmpty()
            .MaximumLength(64)
            .Must(zone => TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _))
            .WithMessage("Unknown time zone.");
    }
}

public sealed class ColorDailyUsageResponse
{
    public Guid FilamentVariantId { get; set; }

    /// <summary>Grams prints and manual usage drew each day, aligned with <see cref="DailyUsageResponse.Days"/>.</summary>
    public IReadOnlyList<decimal> DrawnGrams { get; set; } = [];

    /// <summary>The part of <see cref="DrawnGrams"/> that failed and cancelled prints took.</summary>
    public IReadOnlyList<decimal> WasteGrams { get; set; } = [];
}

public sealed class DailyUsageResponse
{
    /// <summary>The last 30 local days, oldest first, today last.</summary>
    public IReadOnlyList<DateOnly> Days { get; set; } = [];

    /// <summary>Each colour that drew filament on any of those days.</summary>
    public IReadOnlyList<ColorDailyUsageResponse> Colors { get; set; } = [];
}

/// <summary>GET /api/v1/reports/daily-usage — one filament's grams drawn per colour per local day, over the last 30 days.</summary>
[Permission("spools:list")]
public sealed class DailyUsageEndpoint(IQueryHandler<GetDailyUsageQuery, DailyUsage> handler)
    : Endpoint<DailyUsageRequest, DailyUsageResponse>
{
    public override void Configure()
    {
        Get("/api/v1/reports/daily-usage");
        Policies("Permission");
    }

    public override async Task HandleAsync(DailyUsageRequest req, CancellationToken ct)
    {
        var usage = await handler.HandleAsync(new GetDailyUsageQuery(req.FilamentId, TimeZoneInfo.FindSystemTimeZoneById(req.TimeZone)), ct);
        Response = new DailyUsageResponse
        {
            Days = usage.Days,
            Colors = usage.Colors
                .Select(c => new ColorDailyUsageResponse { FilamentVariantId = c.FilamentVariantId, DrawnGrams = c.DrawnGrams, WasteGrams = c.WasteGrams })
                .ToList(),
        };
    }
}
