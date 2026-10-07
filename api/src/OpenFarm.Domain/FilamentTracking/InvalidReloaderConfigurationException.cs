namespace OpenFarm.Domain.FilamentTracking;

public sealed class InvalidReloaderConfigurationException(string message) : Exception(message);
