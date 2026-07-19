using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed record PartFileClassification(PartFileFormat Format, bool RequiresSlicing);

/// <summary>
/// Determines a file's format and whether it needs slicing — synchronous, at submit time
/// (research.md §1): STL always requires slicing; G-code never does; a 3MF is inspected for Bambu's
/// <c>Metadata/slice_info.config</c> zip entry (present only in an already-sliced <c>.gcode.3mf</c>).
/// </summary>
public interface IPartFileClassifier
{
    /// <exception cref="UnsupportedPartFileFormatException">The file's extension doesn't resolve to a recognized format.</exception>
    PartFileClassification Classify(Stream fileStream, string fileName);
}
