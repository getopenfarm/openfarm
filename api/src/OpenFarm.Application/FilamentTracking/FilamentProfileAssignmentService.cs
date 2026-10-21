using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;

namespace OpenFarm.Application.FilamentTracking;

public sealed record FleetFilamentVariant(string Brand, string ModelCode, decimal NozzleDiameterMm, int PrinterCount);
public sealed record FilamentAssignmentFleet(IReadOnlyList<FleetFilamentVariant> Variants, IReadOnlyList<string> PrintersAwaitingNozzle);

public sealed record FilamentProfileAssignmentIssue(FilamentProfileAssignmentInput Assignment, string Message);

public sealed class FilamentProfileAssignmentService(IFilamentProfileRepository profiles, IPrinterRepository printers,
    ProfileCompatibility compatibility, IQueryHandler<GetSlicingDefaultsQuery, SlicingDefaults> slicingDefaults,
    ProfileResolver resolver, ProfileCatalogVersions? catalogVersions = null)
{
    public async Task<FilamentAssignmentFleet> GetFleetAsync(CancellationToken ct)
    {
        var fleet = await printers.ListAsync(ct);
        return new(fleet.Where(p => p.ReportedNozzleDiameterMm is > 0)
            .GroupBy(p => (p.Brand, p.ModelCode, Nozzle: p.ReportedNozzleDiameterMm!.Value))
            .OrderBy(g => g.Key.Brand).ThenBy(g => g.Key.ModelCode).ThenBy(g => g.Key.Nozzle)
            .Select(g => new FleetFilamentVariant(g.Key.Brand, g.Key.ModelCode, g.Key.Nozzle, g.Count())).ToList(),
            fleet.Where(p => p.ReportedNozzleDiameterMm is null).Select(p => p.Name).ToList());
    }

    public async Task<IReadOnlyList<FilamentProfileAssignmentIssue>> GetIssuesAsync(Filament filament, CancellationToken ct)
    {
        var issues = new List<FilamentProfileAssignmentIssue>();
        var pin = await DefaultSlicerAsync(ct);
        var scope = catalogVersions is null ? null : await catalogVersions.GetScopeAsync(ct);
        foreach (var assignment in filament.ProfileAssignments)
        {
            try
            {
                var profile = await profiles.GetByIdAsync(assignment.FilamentProfileId, ct)
                    ?? throw new SlicingProfileUnconfiguredException("The assigned profile no longer exists.");
                await compatibility.ValidateFilamentAsync(await UsableAsync(profile, pin, ct), filament.MaterialType, assignment.Brand,
                    assignment.ModelCode, assignment.NozzleDiameterMm, ct, scope: scope);
            }
            catch (SlicingProfileUnconfiguredException ex)
            {
                issues.Add(new(new(assignment.Brand, assignment.ModelCode, assignment.NozzleDiameterMm, assignment.FilamentProfileId,
                    filament.Variants.FirstOrDefault(v => v.Id == assignment.FilamentVariantId)?.ColorHex), ex.Message));
            }
        }
        return issues;
    }

    public async Task ReplaceAsync(Filament filament, IReadOnlyList<FilamentProfileAssignmentInput> inputs, CancellationToken ct)
    {
        var rows = new List<FilamentProfileAssignment>();
        var pin = await DefaultSlicerAsync(ct);
        var scope = catalogVersions is null ? null : await catalogVersions.GetScopeAsync(ct);
        foreach (var input in inputs)
        {
            if (input.NozzleDiameterMm <= 0 || input.FilamentProfileId == Guid.Empty)
                throw new SlicingProfileUnconfiguredException("Each assignment needs a positive nozzle diameter and a profile.");
            var model = PrinterModelCatalog.Resolve(input.Brand, input.ModelCode);
            var brand = Enum.Parse<PrinterBrand>(input.Brand, true).ToString();
            var profile = await profiles.GetByIdAsync(input.FilamentProfileId, ct)
                ?? throw new FilamentProfileNotFoundException(input.FilamentProfileId);
            await compatibility.ValidateFilamentAsync(await UsableAsync(profile, pin, ct), filament.MaterialType, brand, model.ModelCode, input.NozzleDiameterMm, ct, scope: scope);
            Guid? variantId = null;
            if (input.ColorHex is not null)
                variantId = filament.Variants.FirstOrDefault(v => string.Equals(v.ColorHex, input.ColorHex, StringComparison.OrdinalIgnoreCase))?.Id
                    ?? throw new SlicingProfileUnconfiguredException("A colour override must refer to a colour of this filament.");
            rows.Add(FilamentProfileAssignment.Create(filament.Id, brand, model.ModelCode,
                input.NozzleDiameterMm, profile.Id, variantId));
        }
        if (rows.Select(a => (a.Brand, a.ModelCode, a.NozzleDiameterMm, a.FilamentVariantId)).Distinct().Count() != rows.Count)
            throw new SlicingProfileUnconfiguredException("Duplicate profile assignment for the same model, nozzle and colour.");
        filament.ReplaceProfileAssignments(rows);
    }

    private async Task<SlicerRef?> DefaultSlicerAsync(CancellationToken ct) =>
        (await slicingDefaults.HandleAsync(new(), ct)).Default is { } summary ? new SlicerRef(summary.Engine, summary.Version) : null;

    // What a new part (pinned to the farm default slicer) would slice with: the assigned preset resolved to that slicer's
    // own catalog — or the reason there is none. A preset the default slicer cannot use is not an assignment to keep.
    private async Task<FilamentProfile> UsableAsync(FilamentProfile profile, SlicerRef? pin, CancellationToken ct) =>
        pin is null ? profile : (await resolver.ResolveAsync(profile, pin, ct)).Require();
}
