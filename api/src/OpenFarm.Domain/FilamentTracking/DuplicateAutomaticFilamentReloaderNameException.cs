namespace OpenFarm.Domain.FilamentTracking;

public sealed class DuplicateAutomaticFilamentReloaderNameException(string name, Guid printerId)
    : Exception($"An Automatic Filament Reloader named '{name}' already exists for printer '{printerId}'.")
{
    public string Name { get; } = name;

    public Guid PrinterId { get; } = printerId;
}
