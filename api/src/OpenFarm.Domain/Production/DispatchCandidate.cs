namespace OpenFarm.Domain.Production;

/// <summary>
/// One queued job the dispatcher is considering for a specific idle printer (research.md R4) — built
/// by <c>ProductionDispatchQueryService</c> (Infrastructure) from a SQL query, consumed by the pure
/// <see cref="ProductionDispatchMatcher"/> (Domain). Lives in Domain, not Infrastructure, precisely
/// because the matcher that consumes it must stay framework-agnostic and TDD-able with no DB
/// dependency (constitution Principle I/II) — a Domain type cannot take a parameter defined in
/// Infrastructure.
/// </summary>
public sealed record DispatchCandidate(
    Guid JobId,
    double QueuePosition,
    decimal RequiredNozzleDiameterMm,
    IReadOnlyList<(string? MaterialType, string? ColorHex)> FilamentRequirement);
