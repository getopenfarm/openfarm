using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

/// <summary>
/// A logical preset declared once and stored once per catalog (engine + version) it lives in, over the shared fakes.
/// Every row's raw JSON carries a marker naming its catalog, so a test can prove which catalog's data a slice used.
/// </summary>
public sealed class TwoEngineCatalog
{
    public static readonly SlicerRef Orca231 = new(SlicerEngine.OrcaSlicer, "2.3.1");
    public static readonly SlicerRef Orca242 = new(SlicerEngine.OrcaSlicer, "2.4.2");
    public static readonly SlicerRef Bambu271 = new(SlicerEngine.BambuStudio, "2.7.1");

    public const string BambuVendor = "Bambulab";
    public const string MachineName = "Bambu Lab P1S 0.4 nozzle";

    public FakeMachineProfileRepository Machines { get; } = new();
    public FakeProcessProfileRepository Processes { get; } = new();
    public FakeFilamentProfileRepository Filaments { get; } = new();

    public ProfileResolver Resolver => new(Machines, Processes, Filaments);

    public static ProfileSourceFormat FormatOf(SlicerRef catalog) => ProfileEngineCompatibility.NativeFormat(catalog.Engine)!.Value;

    private static string CompatibleJson(string[]? names) =>
        names is null ? "[]" : "[" + string.Join(",", names.Select(name => $"\"{name}\"")) + "]";

    public static string Marker(string kind, string name, SlicerRef catalog) => $"{kind}|{name}|{catalog.Engine}-{catalog.Version}";

    /// <summary>"Catalog loaded" is read from the machine catalog (ingestion always writes it), so each catalog gets at least one machine row; declaring any preset in a catalog loads it.</summary>
    public void Loaded(params SlicerRef[] catalogs)
    {
        foreach (var catalog in catalogs)
        {
            if (Machines.Rows.Any(m => m.Origin == ProfileOrigin.System && m.SourceFormat == FormatOf(catalog) && m.SourceVersion == catalog.Version))
            {
                continue;
            }

            Machines.Stage(MachineProfile.CreateSystemProfile("fdm_machine_common", FormatOf(catalog), null,
                $$"""{"type":"machine","marker":"{{Marker("MACHINE", "fdm_machine_common", catalog)}}"}""", BambuVendor, null,
                isSelectable: false, vendorMachineModelName: null, includedTemplateNames: null, sourceVersion: catalog.Version));
        }
    }

    public IReadOnlyDictionary<SlicerRef, MachineProfile> Machine(string vendor, string name, string? id, params SlicerRef[] catalogs)
    {
        var rows = new Dictionary<SlicerRef, MachineProfile>();
        foreach (var catalog in catalogs)
        {
            var row = MachineProfile.CreateSystemProfile(name, FormatOf(catalog), null,
                $$"""{"type":"machine","printer_model":"Bambu Lab P1S","nozzle_diameter":["0.4"],"marker":"{{Marker("MACHINE", name, catalog)}}"}""",
                vendor, id, isSelectable: true, vendorMachineModelName: "Bambu Lab P1S", includedTemplateNames: null, sourceVersion: catalog.Version);
            Machines.Stage(row);
            rows[catalog] = row;
        }

        return rows;
    }

    public IReadOnlyDictionary<SlicerRef, ProcessProfile> Process(
        string vendor, string name, string? id, string[]? compatibleMachines, params SlicerRef[] catalogs)
    {
        var rows = new Dictionary<SlicerRef, ProcessProfile>();
        foreach (var catalog in catalogs)
        {
            var row = ProcessProfile.CreateSystemProfile(name, FormatOf(catalog), null,
                $$"""{"type":"process","layer_height":"0.2","marker":"{{Marker("PROCESS", name, catalog)}}"}""",
                vendor, id, isSelectable: true, compatibleMachineNames: compatibleMachines, sourceVersion: catalog.Version);
            Processes.Stage(row);
            rows[catalog] = row;
        }

        Loaded(catalogs);
        return rows;
    }

    public IReadOnlyDictionary<SlicerRef, FilamentProfile> Filament(
        string vendor, string name, string? id, string[]? compatibleMachines, params SlicerRef[] catalogs)
    {
        var rows = new Dictionary<SlicerRef, FilamentProfile>();
        foreach (var catalog in catalogs)
        {
            var row = FilamentProfile.CreateSystemProfile(name, FormatOf(catalog), null,
                $$"""{"type":"filament","filament_type":["PLA"],"hot_plate_temp":["55"],"compatible_printers":{{CompatibleJson(compatibleMachines)}},"marker":"{{Marker("FILAMENT", name, catalog)}}"}""",
                vendor, id, isSelectable: true, compatibleMachineNames: compatibleMachines, materialType: "PLA", includedTemplateNames: null,
                sourceVersion: catalog.Version);
            Filaments.Stage(row);
            rows[catalog] = row;
        }

        Loaded(catalogs);
        return rows;
    }

    /// <summary>The same logical preset the typical test selects, in every catalog given: the P1S machine, "0.20mm Standard" and "Bambu PLA Basic".</summary>
    public (IReadOnlyDictionary<SlicerRef, MachineProfile> Machine, IReadOnlyDictionary<SlicerRef, ProcessProfile> Process,
        IReadOnlyDictionary<SlicerRef, FilamentProfile> Filament) StandardSet(params SlicerRef[] catalogs) =>
        (Machine(BambuVendor, MachineName, "GM014", catalogs),
            Process(BambuVendor, "0.20mm Standard @BBL X1C", "GP004", [MachineName], catalogs),
            Filament(BambuVendor, "Bambu PLA Basic @BBL X1C", "GFSA00", [MachineName], catalogs));
}
