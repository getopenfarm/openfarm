using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

/// <summary>
/// Whether two rows are the same logical preset. A System preset ingested for several slicer versions is one row
/// per version, so comparing row ids would call "Bambu PLA Basic @BBL X1C" in 2.3.1 and in 2.4.2 different
/// profiles; the vendor's <c>setting_id</c> (or, without one, vendor + name) is the identity that survives.
/// </summary>
public static class ProfileIdentity
{
    public static string Key(ICatalogProfile profile) =>
        profile.Origin != ProfileOrigin.System ? $"user:{profile.Id}"
        : profile.ExternalId is { } externalId ? $"system:{profile.SourceFormat}:id:{externalId}"
        : $"system:{profile.SourceFormat}:name:{profile.VendorName}:{profile.Name}";

    public static bool Same(ICatalogProfile left, ICatalogProfile right) =>
        left.Id == right.Id || string.Equals(Key(left), Key(right), StringComparison.Ordinal);
}
