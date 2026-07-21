namespace OpenFarm.Domain.Profiles;

/// <summary>
/// One organization's default <c>FilamentProfile</c> for a given resolved material type (e.g.
/// "PLA", "PETG") — one row per (OrganizationId, MaterialType). Filament compatibility isn't
/// enforced by OrcaSlicer/Bambu Studio at slice time (unlike machine/process pairing), so this can
/// safely be a flat org-wide map rather than tied to any specific machine.
/// </summary>
public sealed class OrganizationFilamentDefault
{
    private OrganizationFilamentDefault(
        Guid id, Guid organizationId, string materialType, Guid filamentProfileId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        MaterialType = materialType;
        FilamentProfileId = filamentProfileId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string MaterialType { get; private set; }
    public Guid FilamentProfileId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static OrganizationFilamentDefault Create(Guid organizationId, string materialType, Guid filamentProfileId)
    {
        if (string.IsNullOrWhiteSpace(materialType))
        {
            throw new ArgumentException("Material type is required.", nameof(materialType));
        }

        return new OrganizationFilamentDefault(Guid.NewGuid(), organizationId, materialType, filamentProfileId, DateTimeOffset.UtcNow);
    }

    public void ReplaceFilamentProfile(Guid filamentProfileId) => FilamentProfileId = filamentProfileId;
}
