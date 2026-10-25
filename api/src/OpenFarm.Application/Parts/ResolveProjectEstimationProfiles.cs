using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Parts;

/// <summary>
/// Estimates a 3MF project from the presets it names — the printer, process and filament it was saved with — looked up in the
/// pinned slicer's own catalog, for when the slicer rejects the project's embedded settings (saved by a newer version). The
/// estimate then reflects those presets as the pinned slicer ships them, without the project's own edits. A name with no single
/// match in the catalog blocks, never substituting another preset; the checks are <see cref="ResolveEstimationProfiles"/>'s.
/// </summary>
public sealed class ResolveProjectEstimationProfiles(
    IMachineProfileRepository machines, IProcessProfileRepository processes, IFilamentProfileRepository filaments,
    ResolveEstimationProfiles estimation)
{
    public const string Source = "ProjectPresets";

    /// <param name="project">What the project saved: <see cref="EstimationDetails.MachineProfileName"/>, process, filament and build plate names.</param>
    /// <exception cref="SlicingProfileUnconfiguredException">The project does not name usable presets in the pin's catalog.</exception>
    public async Task<(EstimationConfiguration Configuration, EstimationDetails Details)> ResolveAsync(
        EstimationDetails project, SlicerRef pin, CancellationToken cancellationToken)
    {
        var format = pin.Engine switch
        {
            SlicerEngine.OrcaSlicer => ProfileSourceFormat.OrcaSlicer,
            SlicerEngine.BambuStudio => ProfileSourceFormat.BambuStudio,
            _ => throw new SlicingProfileUnconfiguredException("This slicer does not support the project preset formats."),
        };
        var filamentNames = (project.FilamentProfileNames ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToList();
        if (filamentNames.Count != 1)
            throw new SlicingProfileUnconfiguredException("The project uses several filament presets; its estimate needs one.");
        var plate = Enum.GetValues<BuildPlateType>().Cast<BuildPlateType?>()
            .FirstOrDefault(p => BuildPlateTypeVendorNames.Resolve(p!.Value) == project.BuildPlate)
            ?? throw new SlicingProfileUnconfiguredException($"The project's build plate '{project.BuildPlate}' is not known.");

        var machine = Single("printer", project.MachineProfileName, pin,
            await machines.FindByNameInCatalogAsync(format, pin.Version, project.MachineProfileName ?? string.Empty, cancellationToken));
        var process = Single("process", project.ProcessProfileName, pin,
            await processes.FindByNameInCatalogAsync(format, pin.Version, project.ProcessProfileName ?? string.Empty, cancellationToken));
        var filament = Single("filament", filamentNames[0], pin,
            await filaments.FindByNameInCatalogAsync(format, pin.Version, filamentNames[0], cancellationToken));

        var (configuration, details) = await estimation.ResolveAsync(
            new EstimationProfileSelection(machine.Id, process.Id, filament.Id, plate), pin, cancellationToken);
        return (configuration, details with { Source = Source, Engine = pin.Engine.ToString(), Version = pin.Version });
    }

    private static TRow Single<TRow>(string kind, string? name, SlicerRef pin, IReadOnlyList<TRow> rows) where TRow : class, ICatalogProfile =>
        rows is [var only]
            ? only
            : throw new SlicingProfileUnconfiguredException(rows.Count == 0
                ? $"The project's {kind} preset '{name}' is not in the {pin.Engine} {pin.Version} catalog."
                : $"The project's {kind} preset '{name}' matches several presets in the {pin.Engine} {pin.Version} catalog.");
}
