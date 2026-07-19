using OpenFarm.Application.Common;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed class TagPartCommandHandler(IPartRepository partRepository, ITagRepository tagRepository)
    : ICommandHandler<TagPartCommand, PartTagsResult>
{
    public async Task<PartTagsResult> HandleAsync(TagPartCommand command, CancellationToken cancellationToken)
    {
        var part = await partRepository.GetByIdAsync(command.PartId, cancellationToken)
            ?? throw new PartNotFoundException(command.PartId);

        var tag = await tagRepository.FindByNameAsync(command.TagName, cancellationToken);
        if (tag is null)
        {
            tag = Tag.Create(Organization.WellKnownId, command.TagName);
            await tagRepository.AddAsync(tag, cancellationToken);
        }

        // Idempotent — tagging with a name the part already carries is a no-op, not a rejection
        // (contracts/part-library-api.md's "get-or-create" framing implies this, not a 409).
        if (part.Tags.All(t => t.Id != tag.Id))
        {
            part.AddTag(tag);
        }

        await partRepository.SaveChangesAsync(cancellationToken);

        return new PartTagsResult(part.Tags.Select(t => t.Name).ToList());
    }
}
