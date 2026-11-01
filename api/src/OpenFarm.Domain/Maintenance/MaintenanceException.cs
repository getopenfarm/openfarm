namespace OpenFarm.Domain.Maintenance;

/// <summary>A maintenance rule was broken: no interval, a future or pre-cycle completion date, a negative carry.</summary>
public sealed class MaintenanceException(string message) : Exception(message);
