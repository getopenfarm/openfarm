using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed class ReloaderConfigurationService(ISpoolRepository spools)
{
    public async Task ApplyAsync(FeedUnit unit, IReadOnlyList<FeedUnit> units, Guid? replacementId,
        Guid? feedingSlotId, bool transferExternalSpool, CancellationToken ct)
    {
        // Validate before changing tracked state.
        var target = replacementId is null ? null : (units.FirstOrDefault(u => u.Id == replacementId
            && u.Kind == FeedUnitKindCatalog.ExternalSpool && u.PrinterId == unit.PrinterId)
            ?? throw new InvalidFeedUnitReplacementException(replacementId.Value));
        if (target is not null && units.Any(u => u.Id != unit.Id && u.ArchivedAtUtc is null
            && u.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader && u.ReplacesFeedUnitId == target.Id))
            throw new InvalidReloaderConfigurationException("This external port already has an active replacement.");
        if (feedingSlotId is not null && !unit.FeedSlots.Any(s => s.Id == feedingSlotId && s.DeactivatedAtUtc is null))
            throw new InvalidReloaderConfigurationException("Select an active slot of this reloader.");
        var externalSpool = target is null ? null : (await spools.FindLoadedInFeedUnitAsync(target.Id, ct)).SingleOrDefault();
        if (externalSpool is not null && transferExternalSpool)
        {
            if (feedingSlotId is null)
                throw new InvalidReloaderConfigurationException("Select a starting slot to transfer the external spool.");
            if (await spools.FindUsingFeedSlotAsync(feedingSlotId.Value, ct) is not null)
                throw new FeedSlotOccupiedException(feedingSlotId.Value);
        }
        unit.ConfigureReplacement(replacementId);
        unit.SelectFeedingSlot(feedingSlotId);
        if (target is not null) target.Hide();
        if (externalSpool is not null)
        {
            externalSpool.ReturnToStorage();
            if (transferExternalSpool) externalSpool.LoadOntoFeedSlot(feedingSlotId!.Value);
        }
    }
}

public sealed record ConfigureReloaderCommand(Guid FeedUnitId, Guid? ReplacesFeedUnitId,
    Guid? FeedingSlotId, bool TransferExternalSpool = true);
public sealed record SelectReloaderSlotCommand(Guid FeedUnitId, Guid? FeedingSlotId);
