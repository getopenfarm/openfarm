using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed record PartFilamentRequirementNarrowingInput(Guid RequirementId, Guid? NarrowFilamentVariantId, Guid? NarrowFilamentProfileId);

public sealed record EditPartCommand(
    Guid PartId,
    string Name,
    IReadOnlyList<string> Tags,
    int UnitsPerPrint,
    IReadOnlyList<PartFilamentRequirementNarrowingInput> FilamentRequirements);

/// <summary>
/// PUT /api/v1/parts/{id} — name, tags, units per print and filament narrowing in one save. Narrowing
/// checks (profile exists, <see cref="PartFilamentSelectionValidator"/>) run only for requirements whose
/// choice actually changed; the requirement's own existence is checked first (404 before any 400).
/// </summary>
public sealed class EditPartCommandHandler(
    IPartRepository partRepository,
    ITagRepository tagRepository,
    IFilamentProfileRepository filamentProfileRepository,
    PartFilamentSelectionValidator filamentValidator,
    ProductionDispatchRequestService dispatchRequests) : ICommandHandler<EditPartCommand, Part>
{
    public async Task<Part> HandleAsync(EditPartCommand command, CancellationToken cancellationToken)
    {
        var part = await partRepository.GetByIdAsync(command.PartId, cancellationToken)
            ?? throw new PartNotFoundException(command.PartId);

        var changedNarrowing = new List<PartFilamentRequirementNarrowingInput>();
        foreach (var input in command.FilamentRequirements)
        {
            var requirement = part.FilamentRequirements.FirstOrDefault(r => r.Id == input.RequirementId)
                ?? throw new PartFilamentRequirementNotFoundException(part.Id, input.RequirementId);
            if (requirement.NarrowFilamentVariantId == input.NarrowFilamentVariantId
                && requirement.NarrowFilamentProfileId == input.NarrowFilamentProfileId)
            {
                continue;
            }

            if (input.NarrowFilamentProfileId is { } profileId
                && await filamentProfileRepository.GetByIdAsync(profileId, cancellationToken) is null)
            {
                throw new InvalidProfileReferenceException("filament", profileId);
            }

            await filamentValidator.ValidateAsync(requirement.MaterialType, input.NarrowFilamentVariantId, cancellationToken);
            changedNarrowing.Add(input);
        }

        part.Rename(command.Name);
        part.SetUnitsPerPrint(command.UnitsPerPrint);

        var tagNames = command.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        foreach (var tag in part.Tags.Where(t => !tagNames.Contains(t.Name, StringComparer.Ordinal)).ToList())
        {
            part.RemoveTag(tag.Id);
        }

        foreach (var name in tagNames.Where(n => part.Tags.All(t => t.Name != n)))
        {
            var tag = await tagRepository.FindByNameAsync(name, cancellationToken);
            if (tag is null)
            {
                tag = Tag.Create(name);
                tagRepository.Stage(tag);
            }

            part.AddTag(tag);
        }

        foreach (var input in changedNarrowing)
        {
            part.NarrowFilamentRequirement(input.RequirementId, input.NarrowFilamentVariantId, input.NarrowFilamentProfileId);
        }

        await partRepository.SaveChangesAsync(cancellationToken);

        if (changedNarrowing.Count > 0) await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return part;
    }
}
