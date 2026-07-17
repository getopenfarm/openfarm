namespace OpenFarm.Application.Profiles;

// FR-013 — the submitted raw JSON edit failed to parse; the previously saved row is left unchanged.
// Mapped to 422 by DomainExceptionHandler.
public sealed class InvalidProfileJsonException(string detail) : Exception(detail);
