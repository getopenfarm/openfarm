namespace OpenFarm.Domain.FilamentTracking;

/// <summary>An RFID tag already belongs to another active spool, or the spool already carries another tag. 409.</summary>
public sealed class SpoolRfidConflictException(string message) : Exception(message);
