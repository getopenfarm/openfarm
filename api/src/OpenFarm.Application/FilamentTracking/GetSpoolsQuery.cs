using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <param name="Search">Whitespace-separated terms; each must match the label, vendor, material, material type or color name (case-insensitive).</param>
/// <param name="Page">1-based; paging applies only when <paramref name="Page"/> or <paramref name="PageSize"/> is set, otherwise every match is returned.</param>
/// <param name="SpoolId">Single-spool lookup, so a mutation does not have to read the whole inventory to return one row.</param>
/// <param name="SpoolIds">The same for several spools at once, such as the ones a single registration added.</param>
public sealed record GetSpoolsQuery(
    SpoolState? State,
    Guid? FilamentId,
    Guid? FilamentVariantId,
    Guid? VendorId,
    string? Search = null,
    Guid? SpoolId = null,
    int? Page = null,
    int? PageSize = null,
    IReadOnlyCollection<Guid>? SpoolIds = null);

/// <summary>One page of spools plus the number of spools matching the filters (equal to <c>Spools.Count</c> when unpaged).</summary>
public sealed record SpoolSummaryPage(IReadOnlyList<SpoolSummary> Spools, int TotalCount);

public sealed record SpoolSummary(
    Guid Id,
    string? Label,
    Guid FilamentVariantId,
    string ColorName,
    string ColorHex,
    string Material,
    string VendorName,
    decimal SpoolWeightGrams,
    decimal RemainingWeightGrams,
    SpoolState State,
    Guid? FeedSlotId,
    string? FeedSlotLabel,
    bool HasUsageOverrun,
    decimal? Price,
    decimal? PricePerKg,
    string? Supplier,
    string? OrderNumber,
    DateOnly? ReceivedOn);
