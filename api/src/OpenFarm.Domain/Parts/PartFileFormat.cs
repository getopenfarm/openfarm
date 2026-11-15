namespace OpenFarm.Domain.Parts;

public enum PartFileFormat
{
    /// <summary>No longer accepted: kept so parts saved earlier still load. They cannot be sliced for production.</summary>
    Stl,
    ThreeMf,
    GCode,
}
