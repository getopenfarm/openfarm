using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class PrinterDetailTelemetryResponse
{
    public decimal? NozzleTempC { get; set; }

    public decimal? BedTempC { get; set; }

    public int? PrintProgressPercent { get; set; }

    public string? CurrentStageLabel { get; set; }

    public int? RemainingTimeMinutes { get; set; }

    public string? CurrentJobName { get; set; }
}

public sealed class AmsTrayResponse
{
    public int SlotIndex { get; set; }

    public bool Empty { get; set; }

    public string? MaterialType { get; set; }

    public string? ColorHex { get; set; }

    public string? SubBrand { get; set; }

    public int? RemainPercent { get; set; }

    public bool Active { get; set; }
}

public sealed class AmsUnitResponse
{
    public int UnitIndex { get; set; }

    public IReadOnlyList<AmsTrayResponse> Trays { get; set; } = [];
}

public sealed class PrinterDetailResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Brand { get; set; } = string.Empty;

    public string ModelCode { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public bool IsReachable { get; set; }

    public PrinterDetailTelemetryResponse Telemetry { get; set; } = new();

    public IReadOnlyList<AmsUnitResponse> AmsUnits { get; set; } = [];

    /// <summary>Null when unlinked — no printer with a null value here has a resolvable nozzle diameter (FR-005a/FR-005b).</summary>
    public Guid? MachineProfileId { get; set; }

    public string? MachineProfileName { get; set; }
}

[Permission("printers:get-detail")]
public sealed class GetPrinterDetailEndpoint(IQueryHandler<GetPrinterDetailQuery, PrinterDetail?> handler)
    : EndpointWithoutRequest<PrinterDetailResponse>
{
    public override void Configure()
    {
        Get("/api/v1/printers/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var detail = await handler.HandleAsync(new GetPrinterDetailQuery(id), ct);

        if (detail is null)
        {
            await HttpContext.Response.SendNotFoundAsync(ct);
            return;
        }

        Response = new PrinterDetailResponse
        {
            Id = detail.Id,
            Name = detail.Name,
            Brand = detail.Brand,
            ModelCode = detail.ModelCode,
            IpAddress = detail.IpAddress,
            SerialNumber = detail.SerialNumber,
            Status = detail.Status.ToString(),
            IsReachable = detail.IsReachable,
            Telemetry = new PrinterDetailTelemetryResponse
            {
                NozzleTempC = detail.NozzleTempC,
                BedTempC = detail.BedTempC,
                PrintProgressPercent = detail.PrintProgressPercent,
                CurrentStageLabel = detail.CurrentStageLabel,
                RemainingTimeMinutes = detail.RemainingTimeMinutes,
                CurrentJobName = detail.CurrentJobName,
            },
            AmsUnits = detail.AmsUnits
                .Select(unit => new AmsUnitResponse
                {
                    UnitIndex = unit.UnitIndex,
                    Trays = unit.Trays
                        .Select(tray => new AmsTrayResponse
                        {
                            SlotIndex = tray.SlotIndex,
                            Empty = tray.Empty,
                            MaterialType = tray.MaterialType,
                            ColorHex = tray.ColorHex,
                            SubBrand = tray.SubBrand,
                            RemainPercent = tray.RemainPercent,
                            Active = tray.Active,
                        })
                        .ToList(),
                })
                .ToList(),
            MachineProfileId = detail.MachineProfileId,
            MachineProfileName = detail.MachineProfileName,
        };
    }
}
