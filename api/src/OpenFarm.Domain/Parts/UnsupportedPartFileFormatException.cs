namespace OpenFarm.Domain.Parts;

public sealed class UnsupportedPartFileFormatException : Exception
{
    public UnsupportedPartFileFormatException(string fileName)
        : base($"'{fileName}' is not a recognized part file (expected .3mf or .gcode).") => FileName = fileName;

    /// <param name="reason">Why this file is refused, said to the operator as is.</param>
    public UnsupportedPartFileFormatException(string fileName, string reason) : base(reason) => FileName = fileName;

    public string FileName { get; }
}
