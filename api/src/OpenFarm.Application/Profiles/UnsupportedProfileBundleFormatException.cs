namespace OpenFarm.Application.Profiles;

// FR-007 — an unrecognized or corrupted archive upload. Mapped to 422 by DomainExceptionHandler.
public sealed class UnsupportedProfileBundleFormatException(string detail, Exception? innerException = null)
    : Exception(detail, innerException);
