namespace OpenFarm.Domain.Parts;

public sealed class PartFilamentRequirementNotFoundException(Guid partId, Guid requirementId)
    : Exception($"Filament requirement '{requirementId}' was not found on part '{partId}'.")
{
    public Guid PartId { get; } = partId;

    public Guid RequirementId { get; } = requirementId;
}
