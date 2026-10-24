using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class DiscoveredPrinterResponse
{
    public string SerialNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Brand { get; set; } = string.Empty;

    /// <summary>Catalog model code, or null when the device's raw model code is not catalogued.</summary>
    public string? ModelCode { get; set; }

    public string RawModel { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public DateTimeOffset LastSeenAt { get; set; }

    public bool AlreadyRegistered { get; set; }
}

/// <summary>
/// Printers currently announcing themselves on the API host's LAN, used to prefill registration.
/// Reuses <c>printers:register</c> like the catalog endpoint.
/// </summary>
[Permission("printers:register")]
public sealed class GetDiscoveredPrintersEndpoint(
    IQueryHandler<GetDiscoveredPrintersQuery, IReadOnlyList<DiscoveredPrinterResult>> handler)
    : EndpointWithoutRequest<IReadOnlyList<DiscoveredPrinterResponse>>
{
    public override void Configure()
    {
        Get("/api/v1/printers/discovered");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var results = await handler.HandleAsync(new GetDiscoveredPrintersQuery(), ct);

        Response = results
            .Select(r => new DiscoveredPrinterResponse
            {
                SerialNumber = r.Printer.SerialNumber,
                Name = r.Printer.Name,
                Brand = r.Printer.Brand,
                ModelCode = r.Printer.ModelCode,
                RawModel = r.Printer.RawModel,
                IpAddress = r.Printer.IpAddress,
                LastSeenAt = r.Printer.LastSeenAt,
                AlreadyRegistered = r.AlreadyRegistered,
            })
            .ToList();
    }
}
