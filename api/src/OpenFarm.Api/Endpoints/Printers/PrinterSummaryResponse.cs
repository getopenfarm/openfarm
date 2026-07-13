using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class PrinterSummaryResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Brand { get; set; } = string.Empty;

    public string ModelCode { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public bool IsReachable { get; set; }

    public static PrinterSummaryResponse FromDomain(Printer printer) => new()
    {
        Id = printer.Id,
        Name = printer.Name,
        Brand = printer.Brand,
        ModelCode = printer.ModelCode,
        IpAddress = printer.IpAddress,
        SerialNumber = printer.SerialNumber,
        Status = printer.Status.ToString(),
        IsReachable = printer.IsReachable,
    };
}
