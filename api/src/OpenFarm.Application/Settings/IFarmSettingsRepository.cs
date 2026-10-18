using OpenFarm.Domain.Settings;

namespace OpenFarm.Application.Settings;

/// <summary>A farm has exactly one <see cref="FarmSettings"/> row — always exists once FirstBootSeeder has run.</summary>
public interface IFarmSettingsRepository
{
    Task<FarmSettings> GetAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
