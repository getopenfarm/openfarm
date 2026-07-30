using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Tests.Parts.RealSlicer;

/// <summary>One golden-file case's <c>case.json</c> — see Parts/Fixtures/RealSlicer/README.md for the recipe to add a new one.</summary>
internal sealed record GoldenCaseManifest(
    string ModelFileName,
    PartFileFormat ModelFileFormat,
    string ExpectedGCodeFileName,
    string MaterialType,
    decimal NozzleDiameterMm,
    string? MachineProfileName,
    string? ProcessProfileName,
    string? FilamentProfileName,
    // OrcaSlicer's bed/plate type ("curr_bed_type", e.g. "Textured PEI Plate") is a per-slice
    // project setting the GUI applies at slice time — it isn't declared in any machine/process
    // preset file, so it can't come from profile resolution and must be injected as an override
    // when the golden fixture was manually sliced with a non-default plate selected.
    string? BedType)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static GoldenCaseManifest Load(string caseDirectory) =>
        JsonSerializer.Deserialize<GoldenCaseManifest>(
            File.ReadAllText(Path.Combine(caseDirectory, "case.json")), SerializerOptions)
        ?? throw new InvalidOperationException($"case.json in '{caseDirectory}' deserialized to null.");
}
