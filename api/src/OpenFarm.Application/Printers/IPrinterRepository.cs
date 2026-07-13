using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

public interface IPrinterRepository
{
    Task AddAsync(Printer printer, CancellationToken cancellationToken);

    Task<Printer?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Printer?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<Printer>> ListAsync(CancellationToken cancellationToken);

    Task RemoveAsync(Printer printer, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
