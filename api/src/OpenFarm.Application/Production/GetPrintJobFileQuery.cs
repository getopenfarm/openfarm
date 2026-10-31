using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record GetPrintJobFileQuery(Guid JobId);

public sealed class GetPrintJobFileQueryHandler(IPrintJobRepository jobs, IPartRequirementRepository requirements,
    IPartRepository parts, IJobFileResolver resolver, IPartFileStorage? storage = null)
    : IQueryHandler<GetPrintJobFileQuery, JobFileContent>
{
    public async Task<JobFileContent> HandleAsync(GetPrintJobFileQuery query, CancellationToken cancellationToken)
    {
        _ = parts;
        var job = await jobs.GetByIdAsync(query.JobId, cancellationToken) ?? throw new PrintJobNotFoundException(query.JobId);
        var requirement = await requirements.GetByIdAsync(job.PartRequirementId, cancellationToken) ?? throw new PrintJobNotEligibleForRequeueException(job.Id);
        var part = job.GetProductionPart()
            ?? throw new ProductionSnapshotUnavailableException(job.Id, job.SnapshotRecoveryReason);
        if (storage is not null && job.ObjectExecution is { ArtifactPath: not null, ArtifactName: not null } execution)
        {
            await using var retained = await storage.OpenReadAsync(execution.ArtifactPath, cancellationToken);
            using var buffer = new MemoryStream();
            await retained.CopyToAsync(buffer, cancellationToken);
            var actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer.ToArray()));
            if (actualHash != execution.ArtifactHash) throw new IOException("The retained execution artifact checksum changed.");
            return new(buffer.ToArray(), execution.ArtifactName, execution.Manifest.Plate);
        }
        part.EnsurePrintRequirementsComplete();
        return await resolver.ResolveAsync(part, job, job.PrinterId ?? job.PinnedPrinterId ?? Guid.Empty, part.NozzleDiameterMm!.Value, cancellationToken);
    }
}
