using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

// Fixture-replay, test-after (research.md §12/§13) — reads an unsliced 3MF's own
// Metadata/project_settings.config (the OrcaSlicer/Bambu Studio project-settings JSON entry every
// saved project contains, distinct from the sliced-only Metadata/slice_info.config) for its
// filament-preset assignment, plus Metadata/model_settings.config's <plate> elements for plate count.
public sealed partial class UnslicedProjectMaterialResolver : IUnslicedProjectMaterialResolver
{
    public async Task<UnslicedProjectMaterial> ResolveAsync(Stream fileStream, CancellationToken cancellationToken)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or IOException or EndOfStreamException)
        {
            throw new UnslicedProjectMaterialParsingFailedException("The uploaded 3MF could not be read as a valid zip archive.");
        }

        using (archive)
        {
            var settingsEntry = archive.GetEntry("Metadata/project_settings.config");
            if (settingsEntry is null)
            {
                throw new UnslicedProjectMaterialParsingFailedException(
                    "No Metadata/project_settings.config was found — this doesn't look like an OrcaSlicer/Bambu Studio project.");
            }

            string materialType;
            using (var reader = new StreamReader(settingsEntry.Open()))
            {
                var json = await reader.ReadToEndAsync(cancellationToken);
                materialType = ExtractFirstFilamentType(json)
                    ?? throw new UnslicedProjectMaterialParsingFailedException(
                        "The project's own filament-preset assignment (filament_type) could not be parsed.");
            }

            var plateCount = 1;
            var modelSettingsEntry = archive.GetEntry("Metadata/model_settings.config");
            if (modelSettingsEntry is not null)
            {
                using var reader = new StreamReader(modelSettingsEntry.Open());
                var xml = await reader.ReadToEndAsync(cancellationToken);
                var matches = PlateElementRegex().Count(xml);
                if (matches > 0)
                {
                    plateCount = matches;
                }
            }

            return new UnslicedProjectMaterial(materialType, plateCount);
        }
    }

    private static string? ExtractFirstFilamentType(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("filament_type", out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.Array when value.GetArrayLength() > 0 => value[0].GetString(),
                JsonValueKind.String => value.GetString(),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"<plate\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlateElementRegex();
}
