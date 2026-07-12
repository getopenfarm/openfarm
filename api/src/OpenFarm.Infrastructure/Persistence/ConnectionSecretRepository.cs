using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Domain.ConnectionSecrets;

namespace OpenFarm.Infrastructure.Persistence;

public sealed class ConnectionSecretRepository(OpenFarmDbContext dbContext) : IConnectionSecretRepository
{
    public async Task AddAsync(ConnectionSecret connectionSecret, CancellationToken cancellationToken)
    {
        dbContext.ConnectionSecrets.Add(connectionSecret);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConnectionSecret>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.ConnectionSecrets
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}
