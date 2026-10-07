using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed class ConfigureReloaderCommandHandler(IFeedUnitRepository units,
    ReloaderConfigurationService configuration, IFilamentTrackingEventPublisher events)
    : ICommandHandler<ConfigureReloaderCommand, FeedUnit>
{
    public async Task<FeedUnit> HandleAsync(ConfigureReloaderCommand command, CancellationToken cancellationToken)
    {
        var printerId = await units.GetPrinterIdAsync(command.FeedUnitId, cancellationToken)
            ?? throw new FeedUnitNotFoundException(command.FeedUnitId);
        var unit = await units.ExecuteAtomicallyAsync(async () =>
        {
            var unit = await units.GetByIdAsync(command.FeedUnitId, cancellationToken)
                ?? throw new FeedUnitNotFoundException(command.FeedUnitId);
            var all = await units.GetByPrinterIdAsync(unit.PrinterId, cancellationToken);
            await configuration.ApplyAsync(unit, all, command.ReplacesFeedUnitId, command.FeedingSlotId,
                command.TransferExternalSpool, cancellationToken);
            await units.SaveChangesAsync(cancellationToken);
            return unit;
        }, cancellationToken, printerId);
        await events.FeedUnitsChangedAsync(unit.OrganizationId, unit.PrinterId, cancellationToken);
        return unit;
    }
}

public sealed class SelectReloaderChannelCommandHandler(IFeedUnitRepository units, IFilamentTrackingEventPublisher events)
    : ICommandHandler<SelectReloaderChannelCommand, FeedUnit>
{
    public async Task<FeedUnit> HandleAsync(SelectReloaderChannelCommand command, CancellationToken cancellationToken)
    {
        var printerId = await units.GetPrinterIdAsync(command.FeedUnitId, cancellationToken)
            ?? throw new FeedUnitNotFoundException(command.FeedUnitId);
        var unit = await units.ExecuteAtomicallyAsync(async () =>
        {
            var unit = await units.GetByIdAsync(command.FeedUnitId, cancellationToken)
                ?? throw new FeedUnitNotFoundException(command.FeedUnitId);
            unit.SelectFeedingSlot(command.FeedingSlotId);
            await units.SaveChangesAsync(cancellationToken);
            return unit;
        }, cancellationToken, printerId);
        await events.FeedUnitsChangedAsync(unit.OrganizationId, unit.PrinterId, cancellationToken);
        return unit;
    }
}
