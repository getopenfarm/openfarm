using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed class CreateFilamentCommandHandler(
    IFilamentRepository filamentRepository,
    IVendorRepository vendorRepository, FilamentProfileAssignmentService assignments) : ICommandHandler<CreateFilamentCommand, Filament>
{
    public async Task<Filament> HandleAsync(CreateFilamentCommand command, CancellationToken cancellationToken)
    {
        var vendor = await vendorRepository.GetByIdAsync(command.VendorId, cancellationToken)
            ?? throw new VendorNotFoundException(command.VendorId);

        var filament = Filament.Create(vendor.Id, command.Material, command.MaterialType);
        filament.SetPricePerKg(command.PricePerKg);
        filament.SetLowStockGrams(command.LowStockGrams);
        foreach (var input in command.Variants)
        {
            var variant = filament.AddVariant(input.ColorName, input.ColorHex);
            variant.SetPricePerKg(input.PricePerKg);
            variant.SetLowStockGrams(input.LowStockGrams);
        }

        await assignments.ReplaceAsync(filament, command.ProfileAssignments ?? [], cancellationToken);
        await filamentRepository.AddAsync(filament, cancellationToken);

        return filament;
    }
}
