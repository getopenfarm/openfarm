using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;

namespace OpenFarm.Application.FilamentTracking;

public sealed record FleetFilamentVariant(string Brand, string ModelCode, decimal NozzleDiameterMm, int PrinterCount);
public sealed record FilamentAssignmentFleet(IReadOnlyList<FleetFilamentVariant> Variants, IReadOnlyList<string> PrintersAwaitingNozzle);

public sealed record FilamentProfileAssignmentIssue(FilamentProfileAssignmentInput Assignment, string Message);

public sealed class FilamentProfileAssignmentService(IFilamentProfileRepository profiles, IPrinterRepository printers,
    ProfileCompatibility compatibility, IQueryHandler<GetSlicingDefaultsQuery, SlicingDefaults> slicingDefaults)
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
        var engine = (await slicingDefaults.HandleAsync(new(), ct)).Default?.Engine;
        foreach (var assignment in filament.ProfileAssignments)
        {
            try
            {
                var profile = await profiles.GetByIdAsync(assignment.FilamentProfileId, ct)
                    ?? throw new SlicingProfileUnconfiguredException("The assigned profile no longer exists.");
                if (engine is { } selected) ProfileEngineCompatibility.EnsureSupported(selected, profile.SourceFormat);
                await compatibility.ValidateFilamentAsync(profile, filament.MaterialType, assignment.Brand,
                    assignment.ModelCode, assignment.NozzleDiameterMm, ct);
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
        var engine = (await slicingDefaults.HandleAsync(new(), ct)).Default?.Engine;
        foreach (var input in inputs)
        {
            if (input.NozzleDiameterMm <= 0 || input.FilamentProfileId == Guid.Empty)
                throw new SlicingProfileUnconfiguredException("Each assignment needs a positive nozzle diameter and a profile.");
            var model = PrinterModelCatalog.Resolve(input.Brand, input.ModelCode);
            var brand = Enum.Parse<PrinterBrand>(input.Brand, true).ToString();
            var profile = await profiles.GetByIdAsync(input.FilamentProfileId, ct)
                ?? throw new FilamentProfileNotFoundException(input.FilamentProfileId);
            if (engine is { } selected) ProfileEngineCompatibility.EnsureSupported(selected, profile.SourceFormat);
            await compatibility.ValidateFilamentAsync(profile, filament.MaterialType, brand, model.ModelCode, input.NozzleDiameterMm, ct);
            Guid? variantId = null;
            if (input.ColorHex is not null)
                variantId = filament.Variants.FirstOrDefault(v => string.Equals(v.ColorHex, input.ColorHex, StringComparison.OrdinalIgnoreCase))?.Id
                    ?? throw new SlicingProfileUnconfiguredException("A colour override must refer to a colour of this filament.");
            rows.Add(FilamentProfileAssignment.Create(filament.OrganizationId, filament.Id, brand, model.ModelCode,
                input.NozzleDiameterMm, profile.Id, variantId));
        }
        if (rows.Select(a => (a.Brand, a.ModelCode, a.NozzleDiameterMm, a.FilamentVariantId)).Distinct().Count() != rows.Count)
            throw new SlicingProfileUnconfiguredException("Duplicate profile assignment for the same model, nozzle and colour.");
        filament.ReplaceProfileAssignments(rows);
    }
}
