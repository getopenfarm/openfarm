using FastEndpoints;
using Microsoft.AspNetCore.Http;
using OpenFarm.Api.Security;
using OpenFarm.Application.Profiles;
using PreviewProfileImportHandler = OpenFarm.Application.Common.ICommandHandler<
    OpenFarm.Application.Profiles.PreviewProfileImportCommand, OpenFarm.Application.Profiles.PreviewProfileImportResult>;

namespace OpenFarm.Api.Endpoints.Profiles;

public sealed class PreviewProfileImportRequest
{
    public IFormFile File { get; set; } = null!;
}

public sealed class MachinePresetPreviewItem
{
    public string Name { get; set; } = string.Empty;
    public string SourceFormat { get; set; } = string.Empty;
    public string? BaseProfileName { get; set; }
    public bool HasUnresolvedBaseInChain { get; set; }
    public bool IsDuplicate { get; set; }
    public double? NozzleSizeMm { get; set; }
    public double? BedSizeXMm { get; set; }
    public double? BedSizeYMm { get; set; }
    public double? MaxHeightMm { get; set; }
    public bool? HasEnclosure { get; set; }
    public List<string> SupportedMaterials { get; set; } = [];
}

public sealed class FilamentPresetPreviewItem
{
    public string Name { get; set; } = string.Empty;
    public string SourceFormat { get; set; } = string.Empty;
    public string? BaseProfileName { get; set; }
    public bool HasUnresolvedBaseInChain { get; set; }
    public bool IsDuplicate { get; set; }
    public string? Material { get; set; }
    public string? Brand { get; set; }
    public string? ColorHex { get; set; }
    public double? NozzleSizeMm { get; set; }
    public double? NozzleTempC { get; set; }
    public double? BedTempC { get; set; }
    public double? MaxVolumetricSpeedMm3s { get; set; }
    public int? FanPercent { get; set; }
    public List<string> CompatiblePrinterNames { get; set; } = [];
}

public sealed class PreviewProfileImportResponse
{
    public string SourceFileName { get; set; } = string.Empty;
    public List<MachinePresetPreviewItem> MachinePresets { get; set; } = [];
    public List<FilamentPresetPreviewItem> FilamentPresets { get; set; } = [];
}

// POST /api/v1/profiles/imports/preview — parses the uploaded bundle and returns everything it
// contains without persisting anything (FR-001–003, contracts/profiles-api.md).
[Permission("profiles:preview-import")]
public sealed class PreviewProfileImportEndpoint(PreviewProfileImportHandler handler)
    : Endpoint<PreviewProfileImportRequest, PreviewProfileImportResponse>
{
    // Generous for any realistic slicer bundle (dozens of presets) while bounding worst-case memory
    // from an oversized/malicious upload (research.md §6).
    private const long MaxBundleSizeBytes = 20 * 1024 * 1024;

    public override void Configure()
    {
        Post("/api/v1/profiles/imports/preview");
        AllowFileUploads();
        MaxRequestBodySize(MaxBundleSizeBytes);
        Policies("Permission");
    }

    public override async Task HandleAsync(PreviewProfileImportRequest req, CancellationToken ct)
    {
        await using var stream = req.File.OpenReadStream();
        var command = new PreviewProfileImportCommand(stream, req.File.FileName);
        var result = await handler.HandleAsync(command, ct);

        Response = new PreviewProfileImportResponse
        {
            SourceFileName = req.File.FileName,
            MachinePresets = result.MachinePresets.Select(p => new MachinePresetPreviewItem
            {
                Name = p.Name,
                SourceFormat = p.SourceFormat.ToString(),
                BaseProfileName = p.BaseProfileName,
                HasUnresolvedBaseInChain = p.HasUnresolvedBaseInChain,
                IsDuplicate = p.IsDuplicate,
                NozzleSizeMm = p.NozzleSizeMm,
                BedSizeXMm = p.BedSizeXMm,
                BedSizeYMm = p.BedSizeYMm,
                MaxHeightMm = p.MaxHeightMm,
                HasEnclosure = p.HasEnclosure,
                SupportedMaterials = p.SupportedMaterials.ToList(),
            }).ToList(),
            FilamentPresets = result.FilamentPresets.Select(p => new FilamentPresetPreviewItem
            {
                Name = p.Name,
                SourceFormat = p.SourceFormat.ToString(),
                BaseProfileName = p.BaseProfileName,
                HasUnresolvedBaseInChain = p.HasUnresolvedBaseInChain,
                IsDuplicate = p.IsDuplicate,
                Material = p.Material,
                Brand = p.Brand,
                ColorHex = p.ColorHex,
                NozzleSizeMm = p.NozzleSizeMm,
                NozzleTempC = p.NozzleTempC,
                BedTempC = p.BedTempC,
                MaxVolumetricSpeedMm3s = p.MaxVolumetricSpeedMm3s,
                FanPercent = p.FanPercent,
                CompatiblePrinterNames = p.CompatiblePrinterNames.ToList(),
            }).ToList(),
        };
    }
}
