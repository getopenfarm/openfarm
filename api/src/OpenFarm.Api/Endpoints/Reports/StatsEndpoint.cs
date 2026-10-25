using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Reports;

namespace OpenFarm.Api.Endpoints.Reports;

public sealed class StatsRequest
{
    /// <summary>Inclusive start of the range: a local midnight, as an instant.</summary>
    public DateTimeOffset From { get; set; }

    /// <summary>Exclusive end of the range.</summary>
    public DateTimeOffset To { get; set; }

    /// <summary>The IANA time zone the days are counted in, such as <c>Asia/Kuala_Lumpur</c>.</summary>
    public string TimeZone { get; set; } = string.Empty;
}

public sealed class StatsRequestValidator : Validator<StatsRequest>
{
    public const int MaxDays = 366;

    public StatsRequestValidator()
    {
        RuleFor(x => x.To).GreaterThan(x => x.From).WithMessage("To must be after From.");
        RuleFor(x => x).Must(x => x.To - x.From <= TimeSpan.FromDays(MaxDays + 1))
            .WithMessage($"A range covers at most {MaxDays} days.");
        // The database counts days in the zone too, and it knows only IANA names.
        RuleFor(x => x.TimeZone)
            .NotEmpty()
            .MaximumLength(64)
            .Must(zone => TimeZoneInfo.TryFindSystemTimeZoneById(zone, out var found) && found.HasIanaId)
            .WithMessage("Unknown time zone.");
    }
}

/// <summary>GET /api/v1/reports/stats — the farm's prints, filament, printer time and failures over a range, against the one before.</summary>
[Permission("spools:list")]
public sealed class StatsEndpoint(IQueryHandler<GetStatsQuery, StatsReport> handler) : Endpoint<StatsRequest, StatsReport>
{
    public override void Configure()
    {
        Get("/api/v1/reports/stats");
        Policies("Permission");
    }

    public override async Task HandleAsync(StatsRequest req, CancellationToken ct) =>
        Response = await handler.HandleAsync(new GetStatsQuery(req.From, req.To, TimeZoneInfo.FindSystemTimeZoneById(req.TimeZone)), ct);
}
