using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

// Carries the uploaded stream for a stateless parse-and-preview pass — nothing is persisted here
// (FR-001–003, research.md §4).
public sealed record PreviewProfileImportCommand(Stream FileStream, string FileName);

public sealed record PreviewProfileImportResult(
    IReadOnlyList<ParsedMachinePresetView> MachinePresets,
    IReadOnlyList<ParsedFilamentPresetView> FilamentPresets);

// Preview response item — see data-model.md's "Transient (non-persisted) shapes". RawDataJson is
// deliberately not part of this shape (parsed into memory to compute the fields below, then
// discarded) — never sent back to the server; confirm re-parses the file itself (research.md §4).
public sealed record ParsedMachinePresetView(
    string Name,
    ProfileSourceFormat SourceFormat,
    string? BaseProfileName,
    bool HasUnresolvedBaseInChain,
    bool IsDuplicate,
    double? NozzleSizeMm,
    double? BedSizeXMm,
    double? BedSizeYMm,
    double? MaxHeightMm,
    bool? HasEnclosure,
    IReadOnlyList<string> SupportedMaterials);

public sealed record ParsedFilamentPresetView(
    string Name,
    ProfileSourceFormat SourceFormat,
    string? BaseProfileName,
    bool HasUnresolvedBaseInChain,
    bool IsDuplicate,
    string? Material,
    string? Brand,
    string? ColorHex,
    double? NozzleSizeMm,
    double? NozzleTempC,
    double? BedTempC,
    double? MaxVolumetricSpeedMm3s,
    int? FanPercent,
    IReadOnlyList<string> CompatiblePrinterNames);
