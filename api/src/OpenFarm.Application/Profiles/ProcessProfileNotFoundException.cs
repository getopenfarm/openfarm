namespace OpenFarm.Application.Profiles;

public sealed class ProcessProfileNotFoundException(Guid processProfileId)
    : Exception($"Process profile '{processProfileId}' was not found.")
{
    public Guid ProcessProfileId { get; } = processProfileId;
}
