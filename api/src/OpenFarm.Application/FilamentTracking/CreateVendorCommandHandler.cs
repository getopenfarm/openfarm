using OpenFarm.Application.Common;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed class CreateVendorCommandHandler(IVendorRepository vendorRepository) : ICommandHandler<CreateVendorCommand, Vendor>
{
    public async Task<Vendor> HandleAsync(CreateVendorCommand command, CancellationToken cancellationToken)
    {
        var existing = await vendorRepository.GetByNameAsync(command.Name, cancellationToken);
        if (existing is not null)
        {
            throw new DuplicateVendorNameException(command.Name);
        }

        var vendor = Vendor.Create(command.Name);
        await vendorRepository.AddAsync(vendor, cancellationToken);

        return vendor;
    }
}
