using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// R1/R2: resolves every line's part demand through one uniform explosion step (real BOM explosion
/// for an Assembly target, an in-memory single-entry synthesis for a Part target), creates one
/// <see cref="PartRequirement"/> per distinct (line, part) pair, and — only for a finite requirement
/// — eagerly creates every <c>Queued</c> <see cref="PrintJob"/> up front (<c>ceil(Quantity /
/// Part.UnitsPerPrint)</c>), all in one transaction. An unlimited (backfill) line creates zero jobs
/// now; T091 (US3) lazily creates them at dispatch time.
/// </summary>
public sealed class CreateProductionRunCommandHandler(
    IProductionRunRepository runRepository,
    IPartRequirementRepository requirementRepository,
    IPrintJobRepository jobRepository,
    IPartRepository partRepository,
    IAssemblyGraphQueryService graphQueryService, ProductionDispatchRequestService? dispatchRequests = null, IAssemblyRepository? assemblyRepository = null, IPartFileStorage? fileStorage = null) : ICommandHandler<CreateProductionRunCommand, ProductionRun>
{
    public async Task<ProductionRun> HandleAsync(CreateProductionRunCommand command, CancellationToken cancellationToken)
    {
        var lineSpecs = command.Lines
            .Select(l => new RunLineSpec(
                l.TargetKind, l.TargetId, l.Quantity, l.RevisionMode, l.DefaultAssignmentMode,
                l.DefaultPinnedPrinterId, l.BackfillPrinterCap, l.BackfillCumulativeUnitCeiling))
            .ToList();

        var run = ProductionRun.Create(Organization.WellKnownId, command.Name, command.DueDate, command.Priority, command.Notes, lineSpecs);
        await runRepository.AddAsync(run, cancellationToken);

        var nextQueuePosition = await jobRepository.GetMaxQueuePositionAsync(Organization.WellKnownId, cancellationToken) ?? 0;

        foreach (var line in run.Lines)
        {
            var explodedParts = await ResolveExplodedPartsAsync(line, cancellationToken);

            var requirements = explodedParts
                .Select(exploded => PartRequirement.Create(
                    Organization.WellKnownId, line.Id, exploded.PartId, line.Quantity is null ? null : exploded.Quantity))
                .ToList();
            await requirementRepository.AddRangeAsync(requirements, cancellationToken);

            // Resolve placeholders left by the upgrade migration from the actual stored bytes,
            // including backfill Parts before any job can be created for them.
            if (fileStorage is not null)
                foreach (var requirement in requirements)
                {
                    var legacyPart = await partRepository.GetByIdAsync(requirement.PartId, cancellationToken)
                        ?? throw new PartNotFoundException(requirement.PartId);
                    if (legacyPart.RevisionHash.Length == 64) continue;
                    await using var bytes = await fileStorage.OpenReadAsync(legacyPart.StoragePath, cancellationToken);
                    var hash = await PartFileHasher.ComputeHashAsync(bytes, cancellationToken);
                    bytes.Position = 0;
                    await fileStorage.SaveAsync(legacyPart.OrganizationId, legacyPart.RevisionHash, legacyPart.OriginalFileName, bytes, cancellationToken);
                    bytes.Position = 0;
                    var path = await fileStorage.SaveAsync(legacyPart.OrganizationId, hash, legacyPart.OriginalFileName, bytes, cancellationToken);
                    legacyPart.MigrateLegacyFileRevision(hash, path);
                }

            if (line.Quantity is null)
            {
                // R2 — unlimited/backfill line: zero jobs now, lazily created at dispatch time (US3).
                continue;
            }

            foreach (var requirement in requirements)
            {
                var part = await partRepository.GetByIdAsync(requirement.PartId, cancellationToken)
                    ?? throw new PartNotFoundException(requirement.PartId);

                var platesNeeded = (int)Math.Ceiling(requirement.Quantity!.Value / part.UnitsPerPrint);
                var filamentSpecs = part.FilamentRequirements
                    .Select(r => new JobFilamentRequirementSpec(r.MaterialType, r.DetectedColorHex))
                    .ToList();

                var jobs = new List<PrintJob>(platesNeeded);
                for (var i = 0; i < platesNeeded; i++)
                {
                    nextQueuePosition += 1024;
                    jobs.Add(PrintJob.Create(
                        Organization.WellKnownId, requirement.Id, nextQueuePosition, part.UnitsPerPrint,
                        line.DefaultAssignmentMode, line.DefaultPinnedPrinterId, part.RevisionHash, part.RevisionNumber, filamentSpecs));
                }

                await jobRepository.AddRangeAsync(jobs, cancellationToken);
            }
        }

        await runRepository.SaveChangesAsync(cancellationToken);
        if (dispatchRequests is not null) await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return run;
    }

    /// <summary>research.md R1 — both target kinds converge on the same <see cref="ExplodedPartQuantity"/> shape before any <see cref="PartRequirement"/> is created.</summary>
    private async Task<IReadOnlyList<ExplodedPartQuantity>> ResolveExplodedPartsAsync(RunLine line, CancellationToken cancellationToken)
    {
        if (line.TargetKind == RunLineTargetKind.Assembly)
        {
            if (assemblyRepository is not null && await assemblyRepository.GetByIdAsync(line.TargetId, cancellationToken) is null)
                throw new RunLineTargetNotFoundException(line.TargetKind, line.TargetId);
            // R1/R2's "quantity-1 explosion" for an unlimited line — enumerates the distinct printed
            // parts only; PartRequirement.Quantity is nulled out above regardless of this result.
            var result = await graphQueryService.ExplodeAsync(Organization.WellKnownId, line.TargetId, line.Quantity ?? 1, cancellationToken);
            return result.PrintedParts;
        }

        var part = await partRepository.GetByIdAsync(line.TargetId, cancellationToken)
            ?? throw new PartNotFoundException(line.TargetId);
        return [new ExplodedPartQuantity(part.Id, part.Name, line.Quantity ?? 1)];
    }
}
