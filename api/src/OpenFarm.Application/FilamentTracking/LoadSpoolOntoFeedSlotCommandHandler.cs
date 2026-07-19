using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.FilamentTracking;

/// <summary>
/// Loads a spool onto a Feed Slot (FR-005). Pre-checks the spool is <see cref="SpoolState.InStorage"/>,
/// the target slot isn't deactivated, and no other spool is already <see cref="SpoolState.Using"/> it
/// (ahead of the partial unique index, FR-005/FR-005b); raises a non-blocking
/// <see cref="LoadSpoolResult.RfidMismatchWarning"/> on a detected-material mismatch (FR-021) and a
/// <see cref="LoadSpoolResult.PortSharingNote"/> when loading onto a superseded External Spool slot
/// (FR-022, informational only).
/// </summary>
public sealed class LoadSpoolOntoFeedSlotCommandHandler(
    ISpoolRepository spoolRepository,
    IFeedUnitRepository feedUnitRepository,
    IFilamentRepository filamentRepository,
    IVendorRepository vendorRepository,
    IPrinterRepository printerRepository) : ICommandHandler<LoadSpoolOntoFeedSlotCommand, LoadSpoolResult>
{
    public async Task<LoadSpoolResult> HandleAsync(LoadSpoolOntoFeedSlotCommand command, CancellationToken cancellationToken)
    {
        var spool = await spoolRepository.GetByIdAsync(command.SpoolId, cancellationToken)
            ?? throw new SpoolNotFoundException(command.SpoolId);

        var feedSlot = await feedUnitRepository.GetFeedSlotByIdAsync(command.FeedSlotId, cancellationToken)
            ?? throw new FeedSlotNotFoundException(command.FeedSlotId);

        if (feedSlot.DeactivatedAtUtc is not null)
        {
            throw new FeedSlotDeactivatedException(command.FeedSlotId);
        }

        var alreadyUsing = await spoolRepository.FindUsingFeedSlotAsync(command.FeedSlotId, cancellationToken);
        if (alreadyUsing is not null)
        {
            throw new FeedSlotOccupiedException(command.FeedSlotId);
        }

        var feedUnit = await feedUnitRepository.GetByFeedSlotIdAsync(command.FeedSlotId, cancellationToken)
            ?? throw new FeedSlotNotFoundException(command.FeedSlotId);

        var printer = await printerRepository.GetByIdAsync(feedUnit.PrinterId, cancellationToken)
            ?? throw new PrinterNotFoundException(feedUnit.PrinterId);

        var rfidMismatchWarning = await ResolveRfidMismatchAsync(spool, feedUnit, feedSlot, printer, cancellationToken);
        var portSharingNote = await ResolvePortSharingNoteAsync(feedUnit, printer, cancellationToken);

        spool.LoadOntoFeedSlot(command.FeedSlotId);
        await spoolRepository.SaveChangesAsync(cancellationToken);

        return new LoadSpoolResult(spool, rfidMismatchWarning, portSharingNote);
    }

    private async Task<bool> ResolveRfidMismatchAsync(Spool spool, FeedUnit feedUnit, FeedSlot feedSlot, Printer printer, CancellationToken cancellationToken)
    {
        var detectedTray = FeedUnitDisplay.ResolveDetectedTray(feedUnit, feedSlot, printer);
        if (detectedTray is not { Empty: false })
        {
            return false;
        }

        var variant = await filamentRepository.FindVariantByIdAsync(spool.FilamentVariantId, cancellationToken);
        if (variant is null)
        {
            return false;
        }

        var filaments = await filamentRepository.ListAsync(cancellationToken);
        var filament = filaments.FirstOrDefault(f => f.Variants.Any(v => v.Id == variant.Id));
        if (filament is null)
        {
            return false;
        }

        var vendor = await vendorRepository.GetByIdAsync(filament.VendorId, cancellationToken);

        var materialMismatch = !string.Equals(detectedTray.MaterialType, filament.MaterialType, StringComparison.OrdinalIgnoreCase);
        var colorMismatch = !string.Equals(detectedTray.ColorHex, variant.ColorHex, StringComparison.OrdinalIgnoreCase);
        var subBrandMismatch = detectedTray.SubBrand is not null
            && vendor is not null
            && !string.Equals(detectedTray.SubBrand, vendor.Name, StringComparison.OrdinalIgnoreCase);

        return materialMismatch || colorMismatch || subBrandMismatch;
    }

    private async Task<string?> ResolvePortSharingNoteAsync(FeedUnit feedUnit, Printer printer, CancellationToken cancellationToken)
    {
        if (feedUnit.Kind != FeedUnitKindCatalog.ExternalSpool)
        {
            return null;
        }

        var allUnits = await feedUnitRepository.GetByPrinterIdAsync(feedUnit.PrinterId, cancellationToken);
        var singleFilamentInlet = !PrinterModelCatalog.TryResolve(printer.Brand, printer.ModelCode, out var model) || model!.FilamentInletCount == 1;
        var supersededByFeedUnitId = FeedUnitDisplay.ResolveSupersededByFeedUnitId(feedUnit, allUnits, singleFilamentInlet);
        if (supersededByFeedUnitId is not { } supersedingId)
        {
            return null;
        }

        var supersedingUnit = allUnits.FirstOrDefault(u => u.Id == supersedingId);
        var supersedingLabel = supersedingUnit is not null ? FeedUnitDisplay.UnitLabel(supersedingUnit, printer.Brand) : "another Feed Unit";
        return $"This port may be superseded by {supersedingLabel}";
    }
}
