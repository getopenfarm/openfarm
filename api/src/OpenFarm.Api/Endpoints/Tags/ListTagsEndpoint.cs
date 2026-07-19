using FastEndpoints;
using OpenFarm.Api.Security;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;

namespace OpenFarm.Api.Endpoints.Tags;

public sealed class TagResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class ListTagsResponse
{
    public List<TagResponse> Tags { get; set; } = [];
}

// GET /api/v1/tags (contracts/part-library-api.md).
[Permission("tags:list")]
public sealed class ListTagsEndpoint(IQueryHandler<ListTagsQuery, IReadOnlyList<TagSummary>> handler)
    : EndpointWithoutRequest<ListTagsResponse>
{
    public override void Configure()
    {
        Get("/api/v1/tags");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var tags = await handler.HandleAsync(new ListTagsQuery(), ct);
        Response = new ListTagsResponse { Tags = tags.Select(t => new TagResponse { Id = t.Id, Name = t.Name }).ToList() };
    }
}
