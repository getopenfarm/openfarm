using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed record GetSpoolsQuery(SpoolState? State, Guid? FilamentId, Guid? FilamentVariantId, Guid? VendorId);

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
    bool HasUsageOverrun);
