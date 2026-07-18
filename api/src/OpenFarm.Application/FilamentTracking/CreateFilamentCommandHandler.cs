using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Organizations;

namespace OpenFarm.Application.FilamentTracking;

public sealed class CreateFilamentCommandHandler(
    IFilamentRepository filamentRepository,
    IVendorRepository vendorRepository) : ICommandHandler<CreateFilamentCommand, Filament>
{
    public async Task<Filament> HandleAsync(CreateFilamentCommand command, CancellationToken cancellationToken)
    {
        var vendor = await vendorRepository.GetByIdAsync(command.VendorId, cancellationToken)
            ?? throw new VendorNotFoundException(command.VendorId);

        var filament = Filament.Create(Organization.WellKnownId, vendor.Id, command.Material, command.MaterialType, command.FilamentProfileId);
        foreach (var variant in command.Variants)
        {
            filament.AddVariant(variant.ColorName, variant.ColorHex);
        }

        await filamentRepository.AddAsync(filament, cancellationToken);

        return filament;
    }
}
