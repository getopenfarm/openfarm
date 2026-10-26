using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// The bytes a dispatcher actually sends to a printer, plus a display name for the connector's job-name field and the plate
/// of a 3MF project to print (1 for bare G-code). Decided once, here, so the plate sent and the plate whose filament is read agree.
/// </summary>
public sealed record JobFileContent(byte[] Bytes, string FileName, int Plate)
{
    /// <summary>The configuration the file was produced for (<see cref="ProductionProfileReadiness.StampAsync"/>); compared with the live one right before sending.</summary>
    public string? ConfigurationStamp { get; init; }

    /// <summary>The presets the file was sliced with, pin and provenance included; <c>null</c> for a file that was sent as stored.</summary>
    public ResolvedSlicingProfiles? Profiles { get; init; }
}

/// <summary>
/// Resolves a job's sliceable output at dispatch time (014 FR-011): an already-sliced
/// <see cref="Part"/> is sent as stored; a part requiring slicing is run through Phase 4's reusable
/// in-app slicing capability on demand, targeted at the job's actual destination printer, with the
/// result scoped to that job only and never persisted as the part's stored file.
/// </summary>
public interface IJobFileResolver
{
    Task<JobFileContent> ResolveAsync(Part part, PrintJob job, Guid printerId, decimal nozzleDiameterMm, CancellationToken cancellationToken);
}
