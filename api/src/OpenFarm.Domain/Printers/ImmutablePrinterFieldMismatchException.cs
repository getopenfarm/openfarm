namespace OpenFarm.Domain.Printers;

/// <summary>
/// Thrown when an edit request supplies a brand/modelCode/serialNumber value that doesn't match the
/// printer's current value (FR-002/FR-010) — a zero-trust rejection of an attempt to change an
/// identity field the API never exposes a setter for, even against a direct-API-bypassing-the-UI
/// caller. Thrown before any mutation happens.
/// </summary>
public sealed class ImmutablePrinterFieldMismatchException(IReadOnlyList<string> mismatchedFields)
    : Exception(BuildMessage(mismatchedFields))
{
    public IReadOnlyList<string> MismatchedFields { get; } = mismatchedFields;

    private static string BuildMessage(IReadOnlyList<string> mismatchedFields) =>
        $"{string.Join(", ", mismatchedFields)} does not match this printer's registered value and cannot be changed.";
}
