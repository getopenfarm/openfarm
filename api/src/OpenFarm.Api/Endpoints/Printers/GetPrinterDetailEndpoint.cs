using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class PrinterDetailTelemetryResponse
{
    public string? ReportedState { get; set; }
    public DateTimeOffset? StateObservedAtUtc { get; set; }
    public IReadOnlyList<PrinterAlert> ActiveAlerts { get; set; } = [];

    /// <summary>The nozzle every extruder shares; null when they differ (see <see cref="Extruders"/>).</summary>
    public decimal? NozzleDiameterMm { get; set; }
    public DateTimeOffset? NozzleObservedAtUtc { get; set; }

    /// <summary>Some extruder's nozzle is confirmed on the current connection.</summary>
    public bool NozzleConfirmed { get; set; }

    /// <summary>What the printer last reported per extruder; index into the model's extruders.</summary>
    public IReadOnlyList<ExtruderResponse> Extruders { get; set; } = [];

    public decimal? NozzleTempC { get; set; }

    public decimal? BedTempC { get; set; }

    public int? PrintProgressPercent { get; set; }

    public string? CurrentStageLabel { get; set; }

    public int? RemainingTimeMinutes { get; set; }

    public string? CurrentJobName { get; set; }
}

public sealed class ExtruderResponse
{
    public int Index { get; set; }

    public decimal? NozzleDiameterMm { get; set; }

    /// <summary>The nozzle was reported on the current connection.</summary>
    public bool NozzleConfirmed { get; set; }

    public decimal? TempC { get; set; }

    public static IReadOnlyList<ExtruderResponse> From(IReadOnlyList<ExtruderReading>? extruders) =>
        (extruders ?? []).Select(e => new ExtruderResponse
        {
            Index = e.Index,
            NozzleDiameterMm = e.NozzleDiameterMm,
            NozzleConfirmed = e.NozzleConfirmed,
            TempC = e.TempC,
        }).ToList();
}

public sealed class BuildPlateTypeOptionResponse
{
    public string Value { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
}

public sealed class PrinterDetailResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Brand { get; set; } = string.Empty;

    public string ModelCode { get; set; } = string.Empty;

    /// <summary>Null only when the catalog no longer knows this printer's model.</summary>
    public PrinterModelResponse? Model { get; set; }

    public string IpAddress { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public bool IsReachable { get; set; }

    public PrinterDetailTelemetryResponse Telemetry { get; set; } = new();

    /// <summary>The feed units and slots the printer last reported, with what it detects in each.</summary>
    public IReadOnlyList<FeedReadingResponse> FeedReadings { get; set; } = [];

    /// <summary>Optional selected preset; never a source of nozzle diameter.</summary>
    public Guid? MachineProfileId { get; set; }

    public string? MachineProfileName { get; set; }

    public string? MachineProfileIssue { get; set; }

    public Guid? ProcessProfileId { get; set; }

    public string? ProcessProfileName { get; set; }

    /// <summary>Null iff no build plate has been selected yet (012, every printer until an operator acts).</summary>
    public string? BuildPlateType { get; set; }

    /// <summary>Backend-resolved display label (FR-012) — null exactly when <see cref="BuildPlateType"/> is null.</summary>
    public string? BuildPlateTypeLabel { get; set; }

    /// <summary>Always non-empty for a registered printer — derived purely from Brand/ModelCode, independent of MachineProfileId.</summary>
    public IReadOnlyList<BuildPlateTypeOptionResponse> CompatibleBuildPlateTypes { get; set; } = [];
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

        var model = PrinterModelCatalog.Find(detail.Brand, detail.ModelCode);
        Response = new PrinterDetailResponse
        {
            Id = detail.Id,
            Name = detail.Name,
            Brand = detail.Brand,
            ModelCode = detail.ModelCode,
            Model = model is null ? null : PrinterModelResponse.From(model),
            IpAddress = detail.IpAddress,
            SerialNumber = detail.SerialNumber,
            Status = detail.Status.ToString(),
            IsReachable = detail.IsReachable,
            Telemetry = new PrinterDetailTelemetryResponse
            {
                ReportedState = detail.ReportedState,
                StateObservedAtUtc = detail.StateObservedAtUtc,
                ActiveAlerts = detail.ActiveAlerts ?? [],
                NozzleDiameterMm = detail.ReportedNozzleDiameterMm,
                NozzleObservedAtUtc = detail.NozzleObservedAtUtc,
                NozzleConfirmed = detail.NozzleConfirmed,
                Extruders = ExtruderResponse.From(detail.Extruders),
                NozzleTempC = detail.NozzleTempC,
                BedTempC = detail.BedTempC,
                PrintProgressPercent = detail.PrintProgressPercent,
                CurrentStageLabel = detail.CurrentStageLabel,
                RemainingTimeMinutes = detail.RemainingTimeMinutes,
                CurrentJobName = detail.CurrentJobName,
            },
            FeedReadings = FeedReadingResponse.From(detail.FeedReadings, model),
            MachineProfileId = detail.MachineProfileId,
            MachineProfileName = detail.MachineProfileName,
            MachineProfileIssue = detail.MachineProfileIssue,
            ProcessProfileId = detail.ProcessProfileId,
            ProcessProfileName = detail.ProcessProfileName,
            BuildPlateType = detail.BuildPlateType?.ToString(),
            BuildPlateTypeLabel = detail.BuildPlateTypeLabel,
            CompatibleBuildPlateTypes = detail.CompatibleBuildPlateTypes
                .Select(option => new BuildPlateTypeOptionResponse { Value = option.Value.ToString(), Label = option.Label })
                .ToList(),
        };
    }
}
