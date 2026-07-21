namespace OpenFarm.Domain.Parts;

public sealed class UnsupportedPartFileFormatException(string fileName)
    : Exception($"'{fileName}' is not a recognized part file (expected .stl, .3mf, or .gcode).")
{
    public string FileName { get; } = fileName;
}
