using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// Builds the per-printer inputs <see cref="ProductionDispatchMatcher"/> (Domain) needs (research.md
/// R4 steps 1–3): a printer's own resolved nozzle diameter, what it currently has loaded, and the
/// flat, indexed `(OrganizationId, Status, AssignmentMode, QueuePosition)` candidate query — kept as
/// plain data-fetching separate from the pure matching algorithm itself, matching the split
/// <c>AssemblyGraphQueryService</c>/<c>AssemblyGraph</c> already established for Assemblies.
/// </summary>
public sealed class ProductionDispatchQueryService(OpenFarmDbContext dbContext, IProfileBundleParser profileParser)
{
    /// <summary>
    /// A printer with no linked <see cref="Printer.MachineProfileId"/>, or one whose profile has no
    /// parseable nozzle size, matches nothing — mirrors Phase 4's "not a real fleet member" language
    /// for the identical condition (research.md R4 step 1).
    /// </summary>
    public async Task<decimal?> ResolvePrinterNozzleDiameterMmAsync(Printer printer, CancellationToken cancellationToken)
    {
        if (printer.MachineProfileId is not { } machineProfileId)
        {
            return null;
        }

        var profile = await dbContext.MachineProfiles.FirstOrDefaultAsync(p => p.Id == machineProfileId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var nozzleSizeMm = profileParser.ExtractMachineDisplayFields(profile.RawDataJson, profile.SourceFormat).NozzleSizeMm;
        return nozzleSizeMm is { } value ? (decimal)value : null;
    }

    /// <summary>
    /// Operator-tracked <see cref="Spool"/> assignment (via <see cref="FeedSlot"/>/<see cref="FeedUnit"/>)
    /// is the primary source; a printer with no tracked spool at all falls back to its connector-
    /// reported <see cref="Printer.AmsUnits"/>/<see cref="Printer.ExternalSpoolTray"/> telemetry
    /// (research.md R4 step 2) — there is no unified read model combining both today, so this method
    /// is that join.
    /// </summary>
    public async Task<IReadOnlyList<(string? MaterialType, string? ColorHex)>> ResolveLoadedMaterialsAsync(
        Printer printer, CancellationToken cancellationToken)
    {
        var feedSlotIds = await dbContext.FeedUnits
            .Where(u => u.PrinterId == printer.Id)
            .SelectMany(u => u.FeedSlots)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var trackedMaterials = await (
            from spool in dbContext.Spools
            where spool.State == SpoolState.Using && spool.FeedSlotId != null && feedSlotIds.Contains(spool.FeedSlotId!.Value)
            join variant in dbContext.FilamentVariants on spool.FilamentVariantId equals variant.Id
            join filament in dbContext.Filaments on variant.FilamentId equals filament.Id
            select new { filament.MaterialType, variant.ColorHex })
            .ToListAsync(cancellationToken);

        if (trackedMaterials.Count > 0)
        {
            return trackedMaterials.Select(m => ((string?)m.MaterialType, (string?)m.ColorHex)).ToList();
        }

        var loaded = new List<(string?, string?)>();
        foreach (var unit in printer.AmsUnits)
        {
            foreach (var tray in unit.Trays)
            {
                if (!tray.Empty)
                {
                    loaded.Add((tray.MaterialType, tray.ColorHex));
                }
            }
        }

        if (printer.ExternalSpoolTray is { Empty: false } externalTray)
        {
            loaded.Add((externalTray.MaterialType, externalTray.ColorHex));
        }

        return loaded;
    }

    /// <summary>
    /// Every <c>Queued</c>/<c>Unassigned</c> job not already dispatch-in-flight to some other printer,
    /// ordered by <see cref="PrintJob.QueuePosition"/>, backed by the
    /// `(OrganizationId, Status, AssignmentMode, QueuePosition)` index (<c>PrintJobConfiguration</c>)
    /// — an indexed SQL query, not an in-memory scan (research.md R4's scale note). The
    /// <see cref="PrintJob.DispatchedPrinterId"/> exclusion closes the race a naive Status/AssignmentMode-only
    /// filter would leave open: a job stays <c>Queued</c>/<c>Unassigned</c> during the dispatch-in-flight
    /// window between <see cref="PrintJob.Dispatch(Guid)"/> and <c>IJobTelemetrySync</c>'s eventual
    /// <c>Printing</c> confirmation, so without this it could be matched a second time to a different
    /// idle printer during that window. Tenant scoping is Finbuckle's global query filter alone, already
    /// applied to this scoped <see cref="OpenFarmDbContext"/> by the caller's tenant-scope setup.
    /// </summary>
    public async Task<IReadOnlyList<DispatchCandidate>> GetUnassignedCandidatesAsync(CancellationToken cancellationToken)
    {
        var rows = await (
            from job in dbContext.PrintJobs
            join requirement in dbContext.PartRequirements on job.PartRequirementId equals requirement.Id
            join part in dbContext.Parts on requirement.PartId equals part.Id
            where job.Status == PrintJobStatus.Queued && job.AssignmentMode == AssignmentMode.Unassigned && job.DispatchedPrinterId == null
            orderby job.QueuePosition
            select new { JobId = job.Id, job.QueuePosition, PartId = part.Id, part.NozzleDiameterMm })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var partIds = rows.Select(r => r.PartId).Distinct().ToList();
        var filamentRequirementsByPart = await dbContext.PartFilamentRequirements
            .Where(r => partIds.Contains(r.PartId))
            .GroupBy(r => r.PartId)
            .ToDictionaryAsync(
                g => g.Key,
                g => (IReadOnlyList<(string?, string?)>)g.Select(r => (r.MaterialType, r.DetectedColorHex)).ToList(),
                cancellationToken);

        return rows
            .Select(row => new DispatchCandidate(
                row.JobId,
                row.QueuePosition,
                row.NozzleDiameterMm,
                filamentRequirementsByPart.GetValueOrDefault(row.PartId, [])))
            .ToList();
    }
}
