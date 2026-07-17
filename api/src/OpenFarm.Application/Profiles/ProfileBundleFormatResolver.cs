using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

// SourceFormat is resolved from the uploaded file's extension (FR-001), not sniffed from bundle
// content — data-model.md establishes SourceFormat isn't derivable from a preset's own JSON, and
// that's equally true of the bundle container itself.
public static class ProfileBundleFormatResolver
{
    public static ProfileSourceFormat Resolve(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".zip" or ".orca_printer" or ".orca_filament" => ProfileSourceFormat.OrcaSlicer,
            ".bbscfg" or ".bbsflmt" => ProfileSourceFormat.BambuStudio,
            _ => throw new UnsupportedProfileBundleFormatException(
                $"'{fileName}' has an unrecognized extension for an OrcaSlicer or Bambu Studio profile bundle."),
        };
    }
}
