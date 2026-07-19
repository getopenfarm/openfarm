using OpenFarm.Application.Common;

namespace OpenFarm.Application.Parts;

public sealed class ListTagsQueryHandler(ITagRepository tagRepository) : IQueryHandler<ListTagsQuery, IReadOnlyList<TagSummary>>
{
    public async Task<IReadOnlyList<TagSummary>> HandleAsync(ListTagsQuery query, CancellationToken cancellationToken)
    {
        var tags = await tagRepository.ListAsync(cancellationToken);
        return tags.Select(t => new TagSummary(t.Id, t.Name)).ToList();
    }
}
