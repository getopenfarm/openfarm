using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed record EditFilamentCommand(
    Guid FilamentId,
    Guid VendorId,
    string Material,
    string MaterialType,
    IReadOnlyList<CreateFilamentVariantInput> NewVariants,
    IReadOnlyList<FilamentProfileAssignmentInput> ProfileAssignments,
    decimal? PricePerKg = null, bool UpdatePrice = false,
    IReadOnlyList<FilamentVariantPriceInput>? VariantPrices = null,
    decimal? LowStockGrams = null, bool UpdateLowStock = false);

/// <summary>A colour's own price and low-stock threshold; a listed colour gets both values, null clearing either.</summary>
public sealed record FilamentVariantPriceInput(Guid VariantId, decimal? PricePerKg, decimal? LowStockGrams = null);

/// <summary>
/// PUT /api/v1/filaments/{id} — updates vendor/material/type, adds colour variants and replaces profile
/// assignments in one save.
/// Assignments resolve their colour by hex against the filament's variants including the ones added
/// here, exactly as <see cref="CreateFilamentCommandHandler"/> does.
/// </summary>
public sealed class EditFilamentCommandHandler(IFilamentRepository filaments, IVendorRepository vendors,
    FilamentProfileAssignmentService assignments, IPrinterRepository printers, IProductionDispatcher dispatcher)
    : ICommandHandler<EditFilamentCommand, Filament>
{
    public async Task<Filament> HandleAsync(EditFilamentCommand command, CancellationToken cancellationToken)
    {
        var filament = await filaments.GetByIdAsync(command.FilamentId, cancellationToken)
            ?? throw new FilamentNotFoundException(command.FilamentId);
        var vendor = await vendors.GetByIdAsync(command.VendorId, cancellationToken)
            ?? throw new VendorNotFoundException(command.VendorId);

        var prices = command.VariantPrices ?? [];
        if (command.PricePerKg < 0 || prices.Any(p => p.PricePerKg < 0) || command.NewVariants.Any(v => v.PricePerKg < 0))
            throw new ArgumentOutOfRangeException(nameof(command), "Price must be non-negative.");
        if (command.LowStockGrams < 0 || prices.Any(p => p.LowStockGrams < 0) || command.NewVariants.Any(v => v.LowStockGrams < 0))
            throw new ArgumentOutOfRangeException(nameof(command), "Low stock threshold must be non-negative.");
        foreach (var price in prices)
            if (!filament.Variants.Any(v => v.Id == price.VariantId))
                throw new FilamentVariantNotFoundException(price.VariantId);
        if (command.UpdatePrice) filament.SetPricePerKg(command.PricePerKg);
        if (command.UpdateLowStock) filament.SetLowStockGrams(command.LowStockGrams);
        foreach (var price in prices)
        {
            var listed = filament.Variants.Single(v => v.Id == price.VariantId);
            listed.SetPricePerKg(price.PricePerKg);
            listed.SetLowStockGrams(price.LowStockGrams);
        }

        // Before ReplaceAsync: assignments are validated against the (possibly new) material type.
        filament.UpdateDetails(vendor.Id, command.Material, command.MaterialType);

        foreach (var variant in command.NewVariants)
        {
            // Backed by the unique (FilamentId, ColorName) index — checked here so the 409 is a typed Domain
            // exception rather than a raw DB constraint failure; also catches duplicates within this request.
            if (filament.Variants.Any(v => string.Equals(v.ColorName, variant.ColorName, StringComparison.Ordinal)))
            {
                throw new DuplicateFilamentVariantColorException(command.FilamentId, variant.ColorName);
            }

            var added = filament.AddVariant(variant.ColorName, variant.ColorHex);
            added.SetPricePerKg(variant.PricePerKg);
            added.SetLowStockGrams(variant.LowStockGrams);
        }

        await assignments.ReplaceAsync(filament, command.ProfileAssignments, cancellationToken);
        await filaments.SaveChangesAsync(cancellationToken);
        foreach (var printer in await printers.ListAsync(cancellationToken)) dispatcher.RequestDispatchAttempt(printer.Id);
        return filament;
    }
}
