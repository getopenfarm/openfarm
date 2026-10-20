using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;

namespace OpenFarm.Api.Endpoints.CostCatalog;

public sealed class ListCostItemsRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? ItemType { get; set; }
    public string? Category { get; set; }
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
}

public sealed record ListCostItemsResponse(IReadOnlyList<CostItemResponse> Items, int TotalCount);

[Permission("non-printed-components:view")]
public sealed class ListCostItemsEndpoint(INonPrintedComponentRepository items) : Endpoint<ListCostItemsRequest, ListCostItemsResponse>
{
    public override void Configure() { Get("/api/v1/cost-items"); Policies("Permission"); }
    public override async Task HandleAsync(ListCostItemsRequest req, CancellationToken ct)
    {
        var result = await items.ListCatalogAsync(Math.Max(1, req.Page), req.PageSize is < 1 or > 200 ? 25 : req.PageSize,
            req.ItemType, req.Category, ListSort.Parse(req.SortBy, req.SortDirection, "name"), ct);
        Response = new(result.Components.Select(CostItemResponse.FromDomain).ToList(), result.TotalCount);
    }
}

public sealed class CostItemIdRequest { public Guid Id { get; set; } }

[Permission("non-printed-components:view")]
public sealed class GetCostItemEndpoint(CostCatalogService catalog) : Endpoint<CostItemIdRequest, CostItemResponse>
{
    public override void Configure() { Get("/api/v1/cost-items/{id}"); Policies("Permission"); }
    public override async Task HandleAsync(CostItemIdRequest req, CancellationToken ct)
    {
        var item = await catalog.GetAsync(Route<Guid>("id"), ct);
        if (item is null) { await Send.NotFoundAsync(ct); return; }
        Response = CostItemResponse.FromDomain(item);
    }
}

[Permission("non-printed-components:create")]
public sealed class CreateCostItemEndpoint(CostCatalogService catalog) : Endpoint<SaveCostItemRequest, CostItemResponse>
{
    public override void Configure() { Post("/api/v1/cost-items"); Policies("Permission"); }
    public override async Task HandleAsync(SaveCostItemRequest req, CancellationToken ct)
    {
        req.Id = Guid.Empty;
        Response = CostItemResponse.FromDomain(await catalog.SaveAsync(null, req.Name, req.Icon, req.Pricing, ct));
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}

[Permission("non-printed-components:edit")]
public sealed class EditCostItemEndpoint(CostCatalogService catalog) : Endpoint<SaveCostItemRequest, CostItemResponse>
{
    public override void Configure() { Put("/api/v1/cost-items/{id}"); Policies("Permission"); }
    public override async Task HandleAsync(SaveCostItemRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        Response = CostItemResponse.FromDomain(await catalog.SaveAsync(req.Id, req.Name, req.Icon, req.Pricing, ct));
    }
}

[Permission("non-printed-components:delete")]
public sealed class DeleteCostItemEndpoint(CostCatalogService catalog) : Endpoint<OpenFarm.Api.Endpoints.NonPrintedComponents.DeleteNonPrintedComponentRequest>
{
    public override void Configure() { Delete("/api/v1/cost-items/{id}"); Policies("Permission"); }
    public override async Task HandleAsync(OpenFarm.Api.Endpoints.NonPrintedComponents.DeleteNonPrintedComponentRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        await catalog.RemoveAsync(req.Id, ct);
        await HttpContext.Response.SendNoContentAsync(ct);
    }
}
