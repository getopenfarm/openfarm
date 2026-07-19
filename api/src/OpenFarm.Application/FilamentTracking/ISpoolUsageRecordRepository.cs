using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public interface ISpoolUsageRecordRepository
{
    Task AddAsync(SpoolUsageRecord record, CancellationToken cancellationToken);
}
