using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

// Application-defined contract; Infrastructure implements against System.IO.Compression.ZipArchive
// (research.md §2) — mirrors IPrinterConnector's placement, keeping Domain/Application
// framework-agnostic per the constitution's Clean Architecture principle.
public interface IProfileBundleParser
{
    // Full bundle contents for preview (FR-001–003) — never persisted itself. sourceFormat is
    // supplied by the caller (resolved from the uploaded file's extension) rather than sniffed from
    // bundle content — SourceFormat isn't derivable from a preset's own JSON (data-model.md), and
    // that's equally true of the bundle container itself.
    Task<ParsedProfileBundle> ParseAsync(Stream bundleStream, ProfileSourceFormat sourceFormat, CancellationToken cancellationToken);

    // Reused at both preview parse time and every catalog read against a stored row's
    // RawDataJson (research.md §2) — one extraction implementation, many call sites. sourceFormat
    // is required since it isn't derivable from rawDataJson alone (data-model.md).
    MachineProfileDisplayFields ExtractMachineDisplayFields(string rawDataJson, ProfileSourceFormat sourceFormat);

    // hasBaseProfileName distinguishes a root preset (no declared base) from a derived one — needed
    // for FR-016's name-suffix compatible-printer fallback, which only applies to root presets
    // (research.md §11).
    FilamentProfileDisplayFields ExtractFilamentDisplayFields(
        string rawDataJson, ProfileSourceFormat sourceFormat, string name, bool hasBaseProfileName);
}

public sealed record ParsedProfileBundle(
    IReadOnlyList<ParsedMachinePreset> MachinePresets,
    IReadOnlyList<ParsedFilamentPreset> FilamentPresets);

// The parser's own raw extraction of one preset — Name/SourceFormat/BaseProfileName are three of
// the four real persisted columns a confirmed import would write; RawDataJson feeds both display-field
// extraction and (on confirm) persistence. Never itself serialized into the preview API response
// (data-model.md's "Transient (non-persisted) shapes") — the preview handler computes the richer
// ParsedMachinePresetView from these.
public sealed record ParsedMachinePreset(
    string Name, ProfileSourceFormat SourceFormat, string? BaseProfileName, string RawDataJson);

public sealed record ParsedFilamentPreset(
    string Name, ProfileSourceFormat SourceFormat, string? BaseProfileName, string RawDataJson);

public sealed record MachineProfileDisplayFields(
    double? NozzleSizeMm,
    double? BedSizeXMm,
    double? BedSizeYMm,
    double? MaxHeightMm,
    bool? HasEnclosure,
    IReadOnlyList<string> SupportedMaterials);

public sealed record FilamentProfileDisplayFields(
    string? Material,
    string? Brand,
    string? ColorHex,
    double? NozzleSizeMm,
    double? NozzleTempC,
    double? BedTempC,
    double? MaxVolumetricSpeedMm3s,
    int? FanPercent,
    IReadOnlyList<string> CompatiblePrinterNames);
