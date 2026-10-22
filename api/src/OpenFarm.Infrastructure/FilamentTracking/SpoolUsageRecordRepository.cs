using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.FilamentTracking;

public sealed class SpoolUsageRecordRepository(OpenFarmDbContext dbContext) : ISpoolUsageRecordRepository
{
    public async Task AddAsync(SpoolUsageRecord record, CancellationToken cancellationToken)
    {
        dbContext.SpoolUsageRecords.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public void Add(SpoolUsageRecord record) => dbContext.SpoolUsageRecords.Add(record);
}
