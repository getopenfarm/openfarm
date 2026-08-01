namespace OpenFarm.Domain.Printers;

// FR-002 — a build plate is only ever compatible with the exact registered model's own
// PrinterModelCatalog.CompatiblePlateTypes set, never the linked machine profile.
public sealed class IncompatibleBuildPlateException(BuildPlateType requested, string brand, string modelCode)
    : Exception($"'{requested}' is not a compatible build plate for {brand} {modelCode}.")
{
    public BuildPlateType Requested { get; } = requested;

    public string Brand { get; } = brand;

    public string ModelCode { get; } = modelCode;
}
