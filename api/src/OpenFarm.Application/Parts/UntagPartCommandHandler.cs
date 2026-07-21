using OpenFarm.Application.Common;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed class UntagPartCommandHandler(IPartRepository partRepository) : ICommandHandler<UntagPartCommand, PartTagsResult>
{
    public async Task<PartTagsResult> HandleAsync(UntagPartCommand command, CancellationToken cancellationToken)
    {
        var part = await partRepository.GetByIdAsync(command.PartId, cancellationToken)
            ?? throw new PartNotFoundException(command.PartId);

        part.RemoveTag(command.TagId);
        await partRepository.SaveChangesAsync(cancellationToken);

        return new PartTagsResult(part.Tags.Select(t => t.Name).ToList());
    }
}
