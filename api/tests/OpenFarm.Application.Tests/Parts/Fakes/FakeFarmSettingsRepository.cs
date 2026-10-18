using OpenFarm.Application.Settings;
using OpenFarm.Domain.Settings;

namespace OpenFarm.Application.Tests.Parts.Fakes;

public sealed class FakeFarmSettingsRepository(FarmSettings settings) : IFarmSettingsRepository
{
    public Task<FarmSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
