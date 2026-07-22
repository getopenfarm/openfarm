namespace OpenFarm.Application.Printers;

// A printer edit's machineProfileId/processProfileId either doesn't exist, or (process only) isn't a
// System-origin row (FR-011 — a printer can never link a User row as its process profile). Distinct
// from MachineProfileNotFoundException/ProcessProfileNotFoundException (404, a direct GET-by-id
// lookup) — this is a 400 Bad Request, a malformed field in a PATCH body, not a missing resource.
public sealed class InvalidProfileReferenceException(string kind, Guid profileId)
    : Exception($"The {kind} profile '{profileId}' does not exist or cannot be used here.")
{
    public string Kind { get; } = kind;

    public Guid ProfileId { get; } = profileId;
}
