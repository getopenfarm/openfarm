using OpenFarm.Domain.ActivityLog;

namespace OpenFarm.Application.Parts;

// FR-012 (US4) — standalone narrow/change/clear for one filament requirement, addressed by its own
// real Id (contracts/profiles-api.md's PATCH .../filament-requirements/{requirementId}).
public sealed record UpdatePartFilamentRequirementNarrowingCommand(
    Guid PartId,
    Guid RequirementId,
    Guid? NarrowFilamentVariantId,
    Guid? NarrowFilamentProfileId,
    Guid? ActorId,
    ActorType ActorType);
