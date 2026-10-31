using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// R1/R2, shared by creating a run and adding lines to one: resolves every new line's part demand through one uniform
/// explosion step (real BOM explosion for an Assembly target, a single entry for a Part target), creates one
/// <see cref="PartRequirement"/> per distinct (line, part) pair, and — only for a finite requirement — eagerly creates
/// every <c>Queued</c> <see cref="PrintJob"/> (<c>ceil(Quantity / Part.UnitsPerPrint)</c>) at the end of the queue.
/// An unlimited (backfill) line creates zero jobs now; dispatch creates them lazily (US3). Nothing is saved here.
/// </summary>
internal sealed class ProductionDemandMaterializer(
    IPartRequirementRepository requirementRepository,
    IPrintJobRepository jobRepository,
    IPartRepository partRepository,
    IAssemblyGraphQueryService graphQueryService,
    IPrinterRepository? printerRepository,
    IAssemblyRepository? assemblyRepository,
    IPartFileStorage? fileStorage,
    ProductionSnapshotFactory? snapshotFactory, ProductionPlanner? planner = null)
{
    /// <summary>Checks every line before anything is staged, so one bad line rejects the whole request.</summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ExplodedPartQuantity>>> ExplodeAsync(
        IReadOnlyList<ProductionDemand> lines, CancellationToken cancellationToken)
    {
        var explodedByLine = new Dictionary<Guid, IReadOnlyList<ExplodedPartQuantity>>();
        foreach (var line in lines)
        {
            if (printerRepository is not null && line.DefaultPinnedPrinterId is { } printerId
                && await printerRepository.GetByIdAsync(printerId, cancellationToken) is null)
                throw new PrinterNotFoundException(printerId);

            var exploded = planner is not null && line.Quantity is { } quantity
                ? (await planner.PlanAsync(new(line.TargetKind, line.TargetId, quantity, line.QuantityBasis), cancellationToken)).Parts
                    .Select(p => new ExplodedPartQuantity(p.PartId, p.Name, p.RequiredUnits)).ToList()
                : await ResolveExplodedPartsAsync(line, cancellationToken);
            if (exploded.Count == 0)
                throw new RunLineHasNoPrintedPartsException(line.TargetId);
            foreach (var item in exploded)
            {
                var part = await partRepository.GetByIdAsync(item.PartId, cancellationToken) ?? throw new PartNotFoundException(item.PartId);
                part.EnsurePrintRequirementsComplete();
                if (planner is not null) await planner.ValidateQuantityAsync(part, cancellationToken);
            }
            line.CaptureTargetName(line.TargetKind == RunLineTargetKind.Part ? exploded[0].PartName
                : assemblyRepository is null ? "Assembly" : (await assemblyRepository.GetByIdAsync(line.TargetId, cancellationToken))!.Name);
            explodedByLine.Add(line.Id, exploded);
        }

        return explodedByLine;
    }

    public async Task StageAsync(
        IReadOnlyList<ProductionDemand> lines, IReadOnlyDictionary<Guid, IReadOnlyList<ExplodedPartQuantity>> explodedByLine,
        CancellationToken cancellationToken, IReadOnlyDictionary<Guid, int>? plateCounts = null)
    {
        var nextQueuePosition = await jobRepository.GetMaxQueuePositionAsync(cancellationToken) ?? 0;

        foreach (var line in lines)
        {
            var explodedParts = explodedByLine[line.Id];

            var requirements = explodedParts
                .Select(exploded => PartRequirement.Create(
                    line.Id, exploded.PartId, line.Quantity is null ? null : exploded.Quantity))
                .ToList();
            foreach (var requirement in requirements)
            {
                var source = explodedParts.First(p => p.PartId == requirement.PartId);
                requirement.CaptureName(source.PartName);
                if (line.Mode == DemandMode.Backfill) requirement.CaptureRecipeRatio(source.Quantity);
            }
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
                    await fileStorage.SaveAsync(legacyPart.RevisionHash, legacyPart.OriginalFileName, bytes, cancellationToken);
                    bytes.Position = 0;
                    var path = await fileStorage.SaveAsync(hash, legacyPart.OriginalFileName, bytes, cancellationToken);
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

                var platesNeeded = plateCounts?.GetValueOrDefault(part.Id) ?? checked((int)Math.Ceiling(requirement.Quantity!.Value / part.UnitsPerPrint));
                var filamentSpecs = part.FilamentRequirements
                    .Select(r => new JobFilamentRequirementSpec(r.MaterialType, r.DetectedColorHex, r.NarrowFilamentVariantId, r.NarrowFilamentProfileId))
                    .ToList();

                var manifest = planner is null ? null : await planner.ReadObjectsAsync(part, cancellationToken);
                var jobs = new List<PrintJob>(platesNeeded);
                for (var i = 0; i < platesNeeded; i++)
                {
                    nextQueuePosition += 1024;
                    jobs.Add(PrintJob.Create(
                        part.Name, requirement.Id, nextQueuePosition, part.UnitsPerPrint,
                        line.DefaultAssignmentMode, line.DefaultPinnedPrinterId, part.RevisionHash, part.RevisionNumber, filamentSpecs));
                }

                for (var i = 0; i < jobs.Count; i++)
                {
                    var target = Math.Min(part.UnitsPerPrint, checked((int)requirement.Quantity!.Value) - i * part.UnitsPerPrint);
                    // Legacy callers without the new planner retain whole-plate materialization.
                    if (planner is not null) jobs[i].SetExactTarget(target, manifest);
                }
                foreach (var job in jobs)
                    if (snapshotFactory is not null) await snapshotFactory.CaptureAsync(job, part, cancellationToken);
                    else job.CaptureProductionSnapshot(part);
                await jobRepository.AddRangeAsync(jobs, cancellationToken);
            }
        }
    }

    private async Task<IReadOnlyList<ExplodedPartQuantity>> ResolveExplodedPartsAsync(ProductionDemand line, CancellationToken cancellationToken)
    {
        if (line.TargetKind == RunLineTargetKind.Assembly)
        {
            if (assemblyRepository is not null && await assemblyRepository.GetByIdAsync(line.TargetId, cancellationToken) is null)
                throw new RunLineTargetNotFoundException(line.TargetKind, line.TargetId);
            // R1/R2's "quantity-1 explosion" for an unlimited line — enumerates the distinct printed
            // parts only; PartRequirement.Quantity is nulled out in StageAsync regardless of this result.
            var result = await graphQueryService.ExplodeAsync(line.TargetId, line.Quantity ?? 1, cancellationToken);
            return result.PrintedParts;
        }

        var part = await partRepository.GetByIdAsync(line.TargetId, cancellationToken)
            ?? throw new PartNotFoundException(line.TargetId);
        return [new ExplodedPartQuantity(part.Id, part.Name, line.QuantityBasis == QuantityBasis.Plates && line.Quantity is { } plates ? checked(plates * part.UnitsPerPrint) : line.Quantity ?? 1)];
    }
}
