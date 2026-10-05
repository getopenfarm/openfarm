using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Infrastructure.Production;

public sealed class PrintJobEligibilityQuery(IPrinterRepository printers, IPartRequirementRepository requirements,
    IPartRepository parts, ProductionDispatchQueryService dispatchQuery, IPrintJobRepository printJobs) : IPrintJobEligibilityQuery
{
    public async Task<IReadOnlySet<Guid>> GetEligibleJobIdsAsync(IReadOnlyList<PrintJob> jobs, CancellationToken cancellationToken)
    {
        var available = new List<(Guid Id, decimal Nozzle, IReadOnlyList<(string?, string?)> Materials)>();
        foreach (var printer in await printers.ListAsync(cancellationToken))
        {
            if (printer.Status != PrinterStatus.Idle || (PrinterModelCatalog.SupportsLiveIntegration(printer.Brand) && !printer.IsReachable)) continue;
            if (await printJobs.GetActiveJobByDispatchedPrinterIdAsync(printer.Id, cancellationToken) is not null) continue;
            var nozzle = await dispatchQuery.ResolvePrinterNozzleDiameterMmAsync(printer, cancellationToken);
            if (nozzle is not null)
                available.Add((printer.Id, nozzle.Value, await dispatchQuery.ResolveLoadedMaterialsAsync(printer, cancellationToken)));
        }
        var nozzles = new Dictionary<Guid, decimal?>();
        var result = new HashSet<Guid>();
        foreach (var job in jobs)
        {
            if (!nozzles.TryGetValue(job.PartRequirementId, out var nozzle))
            {
                var requirement = await requirements.GetByIdAsync(job.PartRequirementId, cancellationToken);
                var part = requirement is null ? null : await parts.GetByIdAsync(requirement.PartId, cancellationToken);
                nozzle = part?.PrintRequirementsStatus == OpenFarm.Domain.Parts.PrintRequirementsStatus.Complete ? part.NozzleDiameterMm : null;
                nozzles[job.PartRequirementId] = nozzle;
            }
            if (nozzle is null) continue;
            var candidate = new DispatchCandidate(job.Id, job.QueuePosition, nozzle.Value,
                job.FilamentConsumption.Select(c => (c.MaterialType, c.DetectedColorHex)).ToList());
            if (available.Any(p => (job.AssignmentMode != AssignmentMode.Pinned || job.PinnedPrinterId == p.Id)
                && ProductionDispatchMatcher.SelectNextJob(p.Nozzle, p.Materials, [candidate]) is not null)) result.Add(job.Id);
        }
        return result;
    }
}
