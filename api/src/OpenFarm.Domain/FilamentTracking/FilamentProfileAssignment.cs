namespace OpenFarm.Domain.FilamentTracking;

public sealed class FilamentProfileAssignment
{
    private FilamentProfileAssignment() { }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid FilamentId { get; private set; }
    public string Brand { get; private set; } = string.Empty;
    public string ModelCode { get; private set; } = string.Empty;
    public decimal NozzleDiameterMm { get; private set; }
    public Guid FilamentProfileId { get; private set; }
    public Guid? FilamentVariantId { get; private set; }

    internal void SetProfile(Guid profileId) => FilamentProfileId = profileId;

    public static FilamentProfileAssignment Create(Guid organizationId, Guid filamentId, string brand,
        string modelCode, decimal nozzleDiameterMm, Guid profileId, Guid? variantId = null)
    {
        if (nozzleDiameterMm <= 0 || profileId == Guid.Empty || string.IsNullOrWhiteSpace(brand) || string.IsNullOrWhiteSpace(modelCode))
            throw new ArgumentException("A model, positive nozzle diameter and profile are required.");
        return new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            FilamentId = filamentId,
            Brand = brand,
            ModelCode = modelCode,
            NozzleDiameterMm = nozzleDiameterMm,
            FilamentProfileId = profileId,
            FilamentVariantId = variantId
        };
    }
}
