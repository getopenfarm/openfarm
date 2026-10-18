namespace OpenFarm.Domain.Production;

/// <summary>Why automatic deduction left a closed print's filament line unrecorded; the operator can act on the first two.</summary>
public enum FilamentSkipReason
{
    /// <summary>No spool is bound to the line.</summary>
    NoSpool,

    /// <summary>The bound spool is not loaded (state is not Using), so it cannot be drawn from.</summary>
    SpoolNotLoaded,

    /// <summary>The slicer's grams for the line could not be read from the file.</summary>
    NoEstimate,

    /// <summary>A stopped print with no layer, percent or curve to say how far it got.</summary>
    NoProgress
}
