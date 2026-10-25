namespace OpenFarm.Domain.Parts;

/// <summary>The headless slicer CLI failed to start, failed, or timed out (research.md §4).</summary>
public sealed class SlicingFailedException(string reason, Exception? innerException = null, int? exitCode = null) : Exception(reason, innerException)
{
    /// <summary>The slicer CLI's exit code, when the failure came from the process exiting.</summary>
    public int? ExitCode { get; } = exitCode;
}
