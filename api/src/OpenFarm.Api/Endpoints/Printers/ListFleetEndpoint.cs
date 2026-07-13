using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class FleetPrinterResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Brand { get; set; } = string.Empty;

    public string ModelCode { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public bool IsReachable { get; set; }

    public int? PrintProgressPercent { get; set; }
}

public sealed class ListFleetResponse
{
    public IReadOnlyList<FleetPrinterResponse> Printers { get; set; } = [];
}

[Permission("printers:list")]
public sealed class ListFleetEndpoint(IQueryHandler<GetFleetQuery, IReadOnlyList<FleetPrinterSummary>> handler)
    : EndpointWithoutRequest<ListFleetResponse>
{
    public override void Configure()
    {
        Get("/api/v1/printers");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var printers = await handler.HandleAsync(new GetFleetQuery(), ct);

        Response = new ListFleetResponse
        {
            Printers = printers
                .Select(p => new FleetPrinterResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Brand = p.Brand,
                    ModelCode = p.ModelCode,
                    IpAddress = p.IpAddress,
                    SerialNumber = p.SerialNumber,
                    Status = p.Status.ToString(),
                    IsReachable = p.IsReachable,
                    PrintProgressPercent = p.PrintProgressPercent,
                })
                .ToList(),
        };
    }
}
