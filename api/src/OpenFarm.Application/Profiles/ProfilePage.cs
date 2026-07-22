namespace OpenFarm.Application.Profiles;

// Shared paginated-result envelope for the three US2 picker queries (research.md §6,
// contracts/profiles-api.md) — one shape regardless of whether pagination was actually requested.
// `Available` is populated only by the process endpoint's `printerId` sugar call
// (contracts/profiles-api.md) — null everywhere else.
public sealed record ProfilePage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, bool? Available = null);
