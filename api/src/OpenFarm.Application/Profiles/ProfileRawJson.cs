using System.Text.Json;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

// FR-012/FR-013 — guard shared by the two raw-data editors. System rows are re-ingested from the slicer
// bundle (an edit would be silently overwritten), and the body must be a JSON *object*: the chain merger
// enumerates its properties, so an array or scalar saved here would make every later read of the row throw.
internal static class ProfileRawJson
{
    public static void EnsureEditable(ProfileOrigin origin, string rawDataJson, string label)
    {
        if (origin == ProfileOrigin.System)
        {
            throw new SystemProfileReadOnlyException($"{label} is a System profile and cannot be edited.");
        }

        try
        {
            using var document = JsonDocument.Parse(rawDataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidProfileJsonException($"{label} raw data must be a JSON object.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidProfileJsonException($"{label} raw data is not valid JSON: {ex.Message}");
        }
    }
}
