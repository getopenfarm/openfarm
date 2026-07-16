namespace OpenFarm.Application.Profiles;

// One persisted row's chain-relevant fields — enough to keep walking without needing the full
// MachineProfile/FilamentProfile entity type (both share this shape, data-model.md).
public sealed record ProfileChainNode(string RawDataJson, Guid? BaseProfileId, string? BaseProfileName);

// Id-matched ancestor-chain walk shared by every read path that needs a profile's resolved
// BaseProfileId chain (research.md §10): the confirm-import preview's catalog-lookup fallback, and
// the list/detail catalog queries. Cycle/depth-guarded — a same-batch mutual reference is the one
// adversarial shape not structurally prevented at write time (research.md §3), neutralized here for
// every read path at once rather than duplicated at write time too.
public static class ProfileChainWalker
{
    private const int MaxChainDepth = 32;

    public delegate Task<ProfileChainNode?> FetchById(Guid id, CancellationToken cancellationToken);

    // Returns the ancestor chain oldest-first (ending with startId's own row), plus whether the walk
    // was cut short by an unresolved/cyclical/too-deep reference anywhere along it.
    public static async Task<(List<string> AncestorsOldestFirst, bool HasUnresolvedBase)> WalkByIdAsync(
        Guid startId, FetchById fetchById, CancellationToken cancellationToken)
    {
        var chainNewestFirst = new List<string>();
        var visited = new HashSet<Guid>();
        var hasUnresolvedBase = false;
        Guid? currentId = startId;
        var depth = 0;

        while (currentId is not null)
        {
            if (depth++ >= MaxChainDepth || !visited.Add(currentId.Value))
            {
                hasUnresolvedBase = true;
                break;
            }

            var node = await fetchById(currentId.Value, cancellationToken);
            if (node is null)
            {
                hasUnresolvedBase = true;
                break;
            }

            chainNewestFirst.Add(node.RawDataJson);
            if (node.BaseProfileId is null && node.BaseProfileName is not null)
            {
                hasUnresolvedBase = true;
            }

            currentId = node.BaseProfileId;
        }

        chainNewestFirst.Reverse();
        return (chainNewestFirst, hasUnresolvedBase);
    }
}
