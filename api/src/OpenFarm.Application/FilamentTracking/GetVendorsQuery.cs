namespace OpenFarm.Application.FilamentTracking;

public sealed record GetVendorsQuery;

public sealed record VendorSummary(Guid Id, string Name);
