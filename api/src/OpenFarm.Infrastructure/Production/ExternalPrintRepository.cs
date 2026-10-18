using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Production;
using OpenFarm.Domain.Production;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Production;

public sealed class ExternalPrintRepository(OpenFarmDbContext dbContext) : IExternalPrintRepository
{
    public Task AddAsync(ExternalPrint print, CancellationToken cancellationToken)
    {
        dbContext.ExternalPrints.Add(print);
        return Task.CompletedTask;
    }

    public Task<ExternalPrint?> GetOpenByPrinterIdAsync(Guid printerId, CancellationToken cancellationToken) =>
        dbContext.ExternalPrints.Include(p => p.Filaments)
            .Where(p => p.PrinterId == printerId && p.Outcome == null)
            .OrderByDescending(p => p.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
