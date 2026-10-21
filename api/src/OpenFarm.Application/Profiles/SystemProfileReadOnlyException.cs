namespace OpenFarm.Application.Profiles;

// A raw-data edit targeted a System profile (ingested from the slicer bundle, never user-editable).
// Mapped to 422 by DomainExceptionHandler.
public sealed class SystemProfileReadOnlyException(string detail) : Exception(detail);
