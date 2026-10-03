using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>The bytes a dispatcher actually sends to a printer, plus a display name for the connector's job-name field.</summary>
public sealed record JobFileContent(byte[] Bytes, string FileName);

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
