using OpenFarm.Application.Printers;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

public sealed class FakePrinterRepository(params Printer[] rows) : IPrinterRepository
{
    private readonly List<Printer> _rows = [.. rows];
    public Task AddAsync(Printer printer, CancellationToken cancellationToken) { _rows.Add(printer); return Task.CompletedTask; }
    public Task<Printer?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_rows.FirstOrDefault(p => p.Id == id));
    public Task<Printer?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken) => Task.FromResult(_rows.FirstOrDefault(p => p.SerialNumber == serialNumber));
    public Task<IReadOnlyList<Printer>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Printer>>(_rows);
    public Task RemoveAsync(Printer printer, CancellationToken cancellationToken) { _rows.Remove(printer); return Task.CompletedTask; }
    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
