using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>
/// Deterministic canned test/dev double (research.md §4) — <see cref="ISlicerEngine"/> is developed
/// and handler-tested against this first; real CLI validation (<see cref="CliSlicerEngine"/>) is
/// manual, never a CI gate, mirroring the constitution's existing <c>BambuLanConnector</c> carve-out.
/// </summary>
public sealed class FakeSlicerEngine : ISlicerEngine
{
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D, 0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    public Task<SlicingResult> EstimateAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat,
        EstimationConfiguration configuration, CancellationToken cancellationToken) => Task.FromResult(Canned());

    public Task<SlicingResult> SliceConfiguredAsync(SlicerRef slicer, Stream modelFileStream, PartFileFormat modelFileFormat,
        string materialType, decimal nozzleDiameterMm, Guid printerId, ResolvedSlicingProfiles profiles, CancellationToken cancellationToken) =>
        Task.FromResult(Canned());

    private static SlicingResult Canned() => new(
        PlateCount: 1,
        EstimatedPrintTimeMinutes: 47,
        EstimatedFilamentWeightGrams: 18.4m,
        Thumbnails: [new ThumbnailSpec(0, OnePixelPng)],
        OutputFileBytes: System.Text.Encoding.UTF8.GetBytes("; fake sliced gcode\n"));
}
