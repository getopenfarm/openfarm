using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Profiles;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed class UpdatePartFilamentRequirementNarrowingCommandHandler(
    IPartRepository partRepository,
    IFilamentProfileRepository filamentProfileRepository,
    IActivityLogWriter activityLogWriter) : ICommandHandler<UpdatePartFilamentRequirementNarrowingCommand, PartFilamentRequirement>
{
    public async Task<PartFilamentRequirement> HandleAsync(UpdatePartFilamentRequirementNarrowingCommand command, CancellationToken cancellationToken)
    {
        var part = await partRepository.GetByIdAsync(command.PartId, cancellationToken)
            ?? throw new PartNotFoundException(command.PartId);

        // Validated before any mutation lands (same zero-partial-success philosophy as
        // EditPrinterCommandHandler's own profile-reference checks).
        if (command.NarrowFilamentProfileId is { } profileId
            && await filamentProfileRepository.GetByIdAsync(profileId, cancellationToken) is null)
        {
            throw new InvalidProfileReferenceException("filament", profileId);
        }

        var requirement = part.NarrowFilamentRequirement(command.RequirementId, command.NarrowFilamentVariantId, command.NarrowFilamentProfileId);
        await partRepository.SaveChangesAsync(cancellationToken);

        var eventType = command.NarrowFilamentVariantId is null && command.NarrowFilamentProfileId is null
            ? "PartFilamentRequirementCleared"
            : "PartFilamentRequirementNarrowed";
        await activityLogWriter.WriteAsync(
            ActivityLogEntry.Create(part.OrganizationId, "Part", part.Id.ToString(), eventType, command.ActorId, command.ActorType),
            cancellationToken);

        return requirement;
    }
}
