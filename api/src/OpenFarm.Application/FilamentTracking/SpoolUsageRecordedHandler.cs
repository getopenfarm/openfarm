using OpenFarm.Application.ActivityLog;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Kind-driven auto-continuation (FR-009–FR-011a, data-model.md's algorithm), invoked whenever a
/// usage record drives a Using spool's RemainingWeightGrams to exactly zero. Marks the depleted spool
/// Finished; the continuation target — an already-loaded sibling FeedSlot — never itself transitions,
/// since it was already Using its own slot the whole time.
/// </summary>
public sealed class SpoolUsageRecordedHandler(
    ISpoolRepository spoolRepository,
    IFeedUnitRepository feedUnitRepository,
    IActivityLogWriter activityLogWriter,
    IFilamentTrackingEventPublisher eventPublisher) : IAutoContinuationResolver
{
    public async Task<AutoContinuationResult> ResolveAsync(Spool depletedSpool, CancellationToken cancellationToken)
    {
        if (depletedSpool.FeedSlotId is not { } feedSlotId)
        {
            return AutoContinuationResult.None;
        }

        var feedUnit = await feedUnitRepository.GetByFeedSlotIdAsync(feedSlotId, cancellationToken);
        if (feedUnit is null)
        {
            return AutoContinuationResult.None;
        }

        var policy = FeedUnitKindCatalog.Resolve(feedUnit.Kind);

        depletedSpool.MarkFinished();
        await spoolRepository.SaveChangesAsync(cancellationToken);

        if (policy == ReloadPolicy.None)
        {
            return AutoContinuationResult.None;
        }

        var siblings = await spoolRepository.FindLoadedInFeedUnitAsync(feedUnit.Id, cancellationToken);
        var candidates = policy == ReloadPolicy.MatchRequired
            ? siblings.Where(s => s.FilamentVariantId == depletedSpool.FilamentVariantId)
            : siblings;

        var continuedSpool = candidates
            .OrderBy(s => SlotPositionOf(feedUnit, s))
            .FirstOrDefault();

        if (continuedSpool is null)
        {
            // Ordinary, expected outcome for MatchRequired's no-match case; a real gap for
            // Unconditional's whole-purpose-is-unattended-continuation case — both log the same
            // event type, since no separate severity field exists yet (Phase 10's dashboard scope).
            await activityLogWriter.WriteAsync(
                ActivityLogEntry.Create(depletedSpool.OrganizationId, "FeedUnit", feedUnit.Id.ToString(), "FeedUnitNoQualifyingReload", null, ActorType.System),
                cancellationToken);
            return AutoContinuationResult.None;
        }

        var materialMismatch = policy == ReloadPolicy.Unconditional && continuedSpool.FilamentVariantId != depletedSpool.FilamentVariantId;

        await activityLogWriter.WriteAsync(
            ActivityLogEntry.Create(depletedSpool.OrganizationId, "FeedUnit", feedUnit.Id.ToString(), "FeedUnitAutoContinued", null, ActorType.System),
            cancellationToken);

        if (materialMismatch)
        {
            await activityLogWriter.WriteAsync(
                ActivityLogEntry.Create(depletedSpool.OrganizationId, "FeedUnit", feedUnit.Id.ToString(), "FeedUnitMaterialMismatch", null, ActorType.System),
                cancellationToken);
        }

        await eventPublisher.FeedUnitAutoContinuedAsync(
            depletedSpool.OrganizationId,
            feedUnit.Id,
            depletedSpool.Id,
            continuedSpool.Id,
            continuedSpool.FeedSlotId!.Value,
            materialMismatch,
            cancellationToken);

        return new AutoContinuationResult(true, continuedSpool.Id, continuedSpool.FeedSlotId, materialMismatch);
    }

    private static int SlotPositionOf(FeedUnit feedUnit, Spool spool) =>
        feedUnit.FeedSlots.First(slot => slot.Id == spool.FeedSlotId).SlotPosition;
}
