using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// <c>Kind = AutomaticFilamentReloader</c>: creates new <see cref="FeedUnit"/>/<see cref="FeedSlot"/>
/// rows (research.md §7). <c>Kind = ExternalSpool</c>: finds the printer's existing row and sets
/// <see cref="FeedUnit.IsRevealed"/> idempotently (research.md §13, FR-024/FR-026).
/// </summary>
public sealed class AddFeedUnitCommandHandler(
    IFeedUnitRepository feedUnitRepository,
    IPrinterRepository printerRepository,
    IFilamentTrackingEventPublisher eventPublisher) : ICommandHandler<AddFeedUnitCommand, FeedUnit>
{
    public async Task<FeedUnit> HandleAsync(AddFeedUnitCommand command, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(command.PrinterId, cancellationToken)
            ?? throw new PrinterNotFoundException(command.PrinterId);

        var existingUnits = await feedUnitRepository.GetByPrinterIdAsync(command.PrinterId, cancellationToken);

        var result = command.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader
            ? await AddAutomaticFilamentReloaderAsync(command, printer.OrganizationId, existingUnits, cancellationToken)
            : await RevealExternalSpoolAsync(command.PrinterId, existingUnits, cancellationToken);

        await eventPublisher.FeedUnitsChangedAsync(printer.OrganizationId, command.PrinterId, cancellationToken);

        return result;
    }

    private async Task<FeedUnit> AddAutomaticFilamentReloaderAsync(
        AddFeedUnitCommand command,
        Guid organizationId,
        IReadOnlyList<FeedUnit> existingUnits,
        CancellationToken cancellationToken)
    {
        if (existingUnits.Any(u => u.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader && string.Equals(u.Name, command.Name, StringComparison.Ordinal)))
        {
            throw new DuplicateAutomaticFilamentReloaderNameException(command.Name!, command.PrinterId);
        }

        Guid? replacesFeedUnitId = null;
        if (command.ReplacesFeedUnitId is { } targetId)
        {
            var target = existingUnits.FirstOrDefault(u => u.Id == targetId && u.Kind == FeedUnitKindCatalog.ExternalSpool)
                ?? throw new InvalidFeedUnitReplacementException(targetId);
            replacesFeedUnitId = target.Id;
        }

        var nextUnitIndex = existingUnits
            .Where(u => u.Kind == FeedUnitKindCatalog.AutomaticFilamentReloader)
            .Select(u => u.UnitIndex)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        var unit = FeedUnit.CreateAutomaticFilamentReloader(organizationId, command.PrinterId, nextUnitIndex, command.Name!, replacesFeedUnitId);
        for (var position = 0; position < command.SlotCount!.Value; position++)
        {
            unit.AddFeedSlot(position);
        }

        await feedUnitRepository.AddAsync(unit, cancellationToken);

        return unit;
    }

    private async Task<FeedUnit> RevealExternalSpoolAsync(Guid printerId, IReadOnlyList<FeedUnit> existingUnits, CancellationToken cancellationToken)
    {
        var externalSpool = existingUnits.FirstOrDefault(u => u.Kind == FeedUnitKindCatalog.ExternalSpool)
            ?? throw new ExternalSpoolNeverReportedException(printerId);

        externalSpool.Reveal();
        await feedUnitRepository.SaveChangesAsync(cancellationToken);

        return externalSpool;
    }
}
