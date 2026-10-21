using System.Linq.Expressions;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

/// <summary>
/// Which presets a read may see: exactly one System catalog version per source format. A format with no entry has no
/// catalog loaded, so none of its System rows are visible. Versions are per-engine namespaces ("2.7.1" can exist for
/// both engines), so a version is always paired with a format.
/// <para>
/// With <see cref="NativeFormat"/> set — what the pickers use, set to the farm default engine's own format — only that
/// format's presets are visible, System and User alike: production only slices with the pinned slicer's own presets,
/// and a part pinned to the other engine reaches that engine's copy of a chosen preset through
/// <see cref="ProfileResolver"/>. Without it (<c>null</c>) User rows of every format stay visible, e.g. for browsing
/// what was imported.
/// </para>
/// </summary>
public sealed record ProfileCatalogScope(IReadOnlyDictionary<ProfileSourceFormat, string> Versions)
{
    /// <summary>The only source format whose presets are listed; <c>null</c> lists User rows of every format.</summary>
    public ProfileSourceFormat? NativeFormat { get; init; }

    public string? VersionFor(ProfileSourceFormat format) => Versions.GetValueOrDefault(format);

    public bool Includes(ProfileOrigin origin, ProfileSourceFormat format, string? sourceVersion)
    {
        if (NativeFormat is { } native && format != native)
        {
            return false;
        }

        return origin == ProfileOrigin.User
            || (VersionFor(format) is { } version && string.Equals(version, sourceVersion, StringComparison.Ordinal));
    }

    /// <summary>
    /// The same rule as a SQL-translatable predicate. Built by property name so one definition serves the three
    /// profile entities, which all expose <c>Origin</c>, <c>SourceFormat</c> and <c>SourceVersion</c>.
    /// </summary>
    public Expression<Func<T, bool>> Predicate<T>() where T : class
    {
        var row = Expression.Parameter(typeof(T), "p");
        Expression Member(string name) => Expression.Property(row, name);
        Expression Format(ProfileSourceFormat format) => Expression.Equal(Member(nameof(MachineProfile.SourceFormat)), Expression.Constant(format));

        Expression userRows = Expression.Equal(Member(nameof(MachineProfile.Origin)), Expression.Constant(ProfileOrigin.User));
        if (NativeFormat is { } native)
        {
            userRows = Expression.AndAlso(userRows, Format(native));
        }

        var body = userRows;
        foreach (var (format, version) in Versions)
        {
            if (NativeFormat is { } only && format != only)
            {
                continue;
            }

            var sameVersion = Expression.Equal(Member(nameof(MachineProfile.SourceVersion)), Expression.Constant(version, typeof(string)));
            body = Expression.OrElse(body, Expression.AndAlso(Format(format), sameVersion));
        }

        return Expression.Lambda<Func<T, bool>>(body, row);
    }
}
