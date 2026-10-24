using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;

namespace OpenFarm.Api.Endpoints.Printers;

public sealed class PrinterCatalogBrandResponse
{
    public string Brand { get; set; } = string.Empty;

    public IReadOnlyList<PrinterModelResponse> Models { get; set; } = [];
}

public sealed class PrinterCatalogResponse
{
    public IReadOnlyList<PrinterCatalogBrandResponse> Brands { get; set; } = [];
}

/// <summary>
/// Backend-exposed printer catalog (User Story 3) — the same (Brand, ModelCode) pairs
/// registration validates against, sourced from <c>PrinterModelCatalog</c> (FR-008). Reuses
/// <c>printers:register</c> (spec Assumptions — no new permission).
/// </summary>
[Permission("printers:register")]
public sealed class GetPrinterCatalogEndpoint(IQueryHandler<GetPrinterCatalogQuery, PrinterCatalogResult> handler)
    : EndpointWithoutRequest<PrinterCatalogResponse>
{
    public override void Configure()
    {
        Get("/api/v1/printers/catalog");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetPrinterCatalogQuery(), ct);

        Response = new PrinterCatalogResponse
        {
            Brands = result.Brands
                .Select(b => new PrinterCatalogBrandResponse { Brand = b.Brand, Models = b.Models.Select(PrinterModelResponse.From).ToList() })
                .ToList(),
        };
    }
}
