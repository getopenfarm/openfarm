using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

/// <summary>
/// The research.md §1 upsert identity key for a System Profile row, re-derived on every ingestion
/// run to match a freshly-fetched vendor preset against an already-ingested row:
/// <list type="bullet">
/// <item>A <b>leaf</b> preset (the vendor always assigns it a stable <c>setting_id</c>) is keyed by
/// <c>(OrganizationId, SourceFormat, Kind, VendorName, ExternalId)</c> — stable across a rename between slicer
/// versions, so re-ingestion upserts the same row rather than duplicating it.</item>
/// <item>An <b>abstract-base</b> preset (no <c>setting_id</c> — it's never itself selectable) is keyed
/// by <c>(OrganizationId, SourceFormat, Kind, VendorName, Name)</c> — the vendor's own within-file inheritance key,
/// the only stable handle such a row has.</item>
/// </list>
/// Two keys of different kinds (leaf vs. abstract-base) are never equal to each other even if one
/// happens to share a string value with the other's discriminating field, because exactly one of
/// <see cref="ExternalId"/>/<see cref="Name"/> is populated per instance — equality is fully
/// structural (record), so this falls out for free rather than needing a custom comparer.
/// </summary>
public readonly record struct SystemProfileIdentityKey
{
    private SystemProfileIdentityKey(Guid organizationId, ProfileKind kind, string vendorName, string? externalId, string? name, ProfileSourceFormat sourceFormat)
    {
        OrganizationId = organizationId;
        SourceFormat = sourceFormat;
        Kind = kind;
        VendorName = vendorName;
        ExternalId = externalId;
        Name = name;
    }

    public Guid OrganizationId { get; }
    public ProfileSourceFormat SourceFormat { get; }
    public ProfileKind Kind { get; }
    public string VendorName { get; }
    public string? ExternalId { get; }
    public string? Name { get; }

    /// <summary>True when this is a leaf key (<see cref="ExternalId"/> set), false for an abstract-base key (<see cref="Name"/> set).</summary>
    public bool IsLeaf => ExternalId is not null;

    /// <summary>
    /// Derives the correct key variant from a freshly-parsed vendor preset: a non-null/non-blank
    /// <paramref name="externalId"/> (the vendor's <c>setting_id</c>) always wins — research.md §0
    /// confirms every leaf preset carries one and no abstract-base preset does, so presence of
    /// <c>setting_id</c> is the discriminator, not <c>instantiation</c> directly (the two are
    /// empirically 1:1, but this reads the more fundamental signal).
    /// </summary>
    public static SystemProfileIdentityKey Create(Guid organizationId, ProfileKind kind, string vendorName, string? externalId, string name,
        ProfileSourceFormat sourceFormat = ProfileSourceFormat.OrcaSlicer)
    {
        if (string.IsNullOrWhiteSpace(vendorName))
        {
            throw new ArgumentException("Vendor name is required.", nameof(vendorName));
        }

        return string.IsNullOrWhiteSpace(externalId)
            ? new SystemProfileIdentityKey(organizationId, kind, vendorName, externalId: null, name: name, sourceFormat: sourceFormat)
            : new SystemProfileIdentityKey(organizationId, kind, vendorName, externalId: externalId, name: null, sourceFormat: sourceFormat);
    }
}
