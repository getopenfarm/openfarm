using OpenFarm.Application.FilamentTracking;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Profiles;

/// <summary>Production uses the assigned printer's tracked spool and hardware-specific assignment only.</summary>
public sealed class RepresentativeFilamentResolver(IFilamentProfileRepository profiles, ISpoolRepository spools,
    IFilamentRepository filaments, IFeedUnitRepository feedUnits, IPrinterRepository printers,
    IMachineProfileRepository machines, ProfileCompatibility compatibility)
{
    public async Task<FilamentMatchResult> ResolveAsync(Guid? printerId, string materialType, CancellationToken cancellationToken)
        => new((await ResolveLoadedAsync(printerId ?? throw new SlicingProfileUnconfiguredException("An assigned printer is required."),
            materialType, null, null, null, cancellationToken)).Profile, FilamentMatchSource.FleetLoaded);

    public async Task<ProductionFilamentSelection> ResolveLoadedAsync(Guid printerId, string? materialType, string? colorHex,
        Guid? narrowedVariantId, Guid? narrowedProfileId, CancellationToken ct)
    {
        var printer = await printers.GetByIdAsync(printerId, ct) ?? throw new PrinterNotFoundException(printerId);
        var machine = printer.MachineProfileId is { } machineId ? await machines.GetByIdAsync(machineId, ct) : null;
        try
        {
            if (machine is null) throw new SlicingProfileUnconfiguredException($"Printer '{printer.Name}' needs a compatible machine profile.");
            await compatibility.ValidateMachineAsync(printer, machine, ct);
        }
        catch (SlicingProfileUnconfiguredException ex)
        { throw new SlicingProfileUnconfiguredException($"{ex.Message} Configure in /printers/{printer.Id}/edit."); }
        var activeSlotIds = ProductionFeedPath.ActiveSlotIds(await feedUnits.GetByPrinterIdAsync(printerId, ct));
        var loaded = await spools.ListAsync(SpoolState.Using, null, ct);
        SlicingProfileUnconfiguredException? issue = null;
        foreach (var spool in loaded.OrderBy(s => s.Id))
        {
            if (spool.FeedSlotId is not { } slotId) continue;
            if (!activeSlotIds.Contains(slotId)) continue;
            var variant = await filaments.FindVariantByIdAsync(spool.FilamentVariantId, ct);
            if (variant is null || narrowedVariantId is { } variantId && variant.Id != variantId
                || narrowedVariantId is null && colorHex is not null && !string.Equals(variant.ColorHex, colorHex, StringComparison.OrdinalIgnoreCase)) continue;
            var filament = await filaments.GetByIdAsync(variant.FilamentId, ct);
            if (filament is null || materialType is not null && filament.MaterialType != materialType) continue;
            try
            {
                var profileId = filament.ResolveProfileId(printer.Brand, printer.ModelCode, printer.ReportedNozzleDiameterMm!.Value, variant.Id)
                    ?? throw new SlicingProfileUnconfiguredException($"Assign a profile for '{filament.Material}' ({variant.ColorName}) on {printer.ModelCode} {printer.ReportedNozzleDiameterMm} mm in /filament/{filament.Id}/edit.");
                if (narrowedProfileId is { } narrowedId && profileId != narrowedId)
                    throw new SlicingProfileUnconfiguredException("The loaded filament assignment does not match the part's required filament profile.");
                var profile = await profiles.GetByIdAsync(profileId, ct)
                    ?? throw new SlicingProfileUnconfiguredException("The assigned filament profile no longer exists.");
                await compatibility.ValidateFilamentAsync(profile, filament.MaterialType, printer.Brand, printer.ModelCode,
                    printer.ReportedNozzleDiameterMm.Value, ct, machine);
                return new(spool.Id, variant.Id, filament.Id, filament.MaterialType, profile);
            }
            catch (SlicingProfileUnconfiguredException ex) { issue ??= new SlicingProfileUnconfiguredException($"{ex.Message} Configure in /filament/{filament.Id}/edit."); }
        }
        if (issue is not null) throw issue;
        throw new SlicingProfileUnconfiguredException($"Register and load a tracked spool for '{materialType ?? "the required material"}'{(colorHex is null ? "" : $" ({colorHex})")} on printer '{printer.Name}'.");
    }
}

public sealed record ProductionFilamentSelection(Guid SpoolId, Guid FilamentVariantId, Guid FilamentId, string MaterialType, FilamentProfile Profile);
