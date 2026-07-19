using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;
using TagPartHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.TagPartCommand, OpenFarm.Application.Parts.PartTagsResult>;
using UntagPartHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Parts.UntagPartCommand, OpenFarm.Application.Parts.PartTagsResult>;

namespace OpenFarm.Api.Endpoints.Parts;

public sealed class TagPartRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed class TagPartRequestValidator : AbstractValidator<TagPartRequest>
{
    public TagPartRequestValidator() => RuleFor(x => x.Name).NotEmpty();
}

public sealed class PartTagsResponse
{
    public List<string> Tags { get; set; } = [];

    public static PartTagsResponse From(PartTagsResult result) => new() { Tags = result.Tags.ToList() };
}

// POST /api/v1/parts/{id}/tags — get-or-creates the Tag (contracts/part-library-api.md).
[Permission("parts:tag")]
public sealed class TagPartEndpoint(TagPartHandler handler) : Endpoint<TagPartRequest, PartTagsResponse>
{
    public override void Configure()
    {
        Post("/api/v1/parts/{id}/tags");
        Policies("Permission");
    }

    public override async Task HandleAsync(TagPartRequest req, CancellationToken ct)
    {
        var partId = Route<Guid>("id");
        var result = await handler.HandleAsync(new TagPartCommand(partId, req.Name), ct);
        Response = PartTagsResponse.From(result);
    }
}

// DELETE /api/v1/parts/{id}/tags/{tagId} (contracts/part-library-api.md).
[Permission("parts:tag")]
public sealed class UntagPartEndpoint(UntagPartHandler handler) : EndpointWithoutRequest<PartTagsResponse>
{
    public override void Configure()
    {
        Delete("/api/v1/parts/{id}/tags/{tagId}");
        Policies("Permission");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var partId = Route<Guid>("id");
        var tagId = Route<Guid>("tagId");
        var result = await handler.HandleAsync(new UntagPartCommand(partId, tagId), ct);
        Response = PartTagsResponse.From(result);
    }
}
