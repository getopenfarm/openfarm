using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public interface ISpoolUsageRecordRepository
{
    /// <summary>Adds the entry and saves immediately — which also saves every other pending change in the same unit of work.</summary>
    Task AddAsync(SpoolUsageRecord record, CancellationToken cancellationToken);

    /// <summary>Tracks the entry without saving, so it commits with the caller's own save (and that save's conflict handling).</summary>
    void Add(SpoolUsageRecord record);
}
