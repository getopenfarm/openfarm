using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Profiles;

// Resolves a declared base name for an imported preset: User rows plus the System rows of the catalog
// version first; when neither has the name, the newest OTHER version that does (an export may reference a
// preset the catalog version no longer lists). Without the version scope, a second ingested version of the
// same engine would make every System base match several rows and leave each import's base unresolved.
internal static class ProfileBaseLookup
{
    private static readonly SlicerVersionComparer VersionComparer = new();

    public static async Task<IReadOnlyList<TRow>> FindAsync<TRow>(
        string? catalogVersion,
        Func<string?, Task<IReadOnlyList<TRow>>> inCatalog,
        Func<Task<IReadOnlyList<TRow>>> anyVersion,
        Func<TRow, string?> versionOf)
    {
        var matches = await inCatalog(catalogVersion);
        if (matches.Count > 0)
        {
            return matches;
        }

        var all = await anyVersion();
        var newest = all.Select(versionOf).Where(version => version is not null).Distinct()
            .OrderByDescending(version => version!, VersionComparer).FirstOrDefault();
        return newest is null ? matches : all.Where(row => versionOf(row) == newest).ToList();
    }
}
