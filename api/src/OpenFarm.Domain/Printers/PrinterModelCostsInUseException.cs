namespace OpenFarm.Domain.Printers;

public sealed class PrinterModelCostsInUseException(string brand, string modelCode)
    : Exception($"Remove all {brand} {modelCode} printers from the fleet before removing its cost settings.");
