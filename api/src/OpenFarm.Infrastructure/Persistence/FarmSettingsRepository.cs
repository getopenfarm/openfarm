using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Settings;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Infrastructure.Persistence;

public sealed class FarmSettingsRepository(OpenFarmDbContext dbContext) : IFarmSettingsRepository
{
    public async Task<FarmSettings> GetAsync(CancellationToken cancellationToken) =>
        await dbContext.FarmSettings.FirstAsync(s => s.Id == FarmSettings.SingletonId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
