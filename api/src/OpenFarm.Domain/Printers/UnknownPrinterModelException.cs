namespace OpenFarm.Domain.Printers;

public sealed class UnknownPrinterModelException(string brand, string modelCode)
    : Exception($"Unknown printer model '{modelCode}' for brand '{brand}'.")
{
    public string Brand { get; } = brand;

    public string ModelCode { get; } = modelCode;
}
