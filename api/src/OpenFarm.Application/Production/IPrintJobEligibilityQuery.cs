using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public interface IPrintJobEligibilityQuery
{
    Task<IReadOnlySet<Guid>> GetEligibleJobIdsAsync(IReadOnlyList<PrintJob> jobs, CancellationToken cancellationToken);
}
