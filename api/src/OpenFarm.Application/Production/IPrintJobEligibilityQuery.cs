using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public interface IPrintJobEligibilityQuery
{
    Task<JobMatchingDiagnostics> GetDiagnosticsAsync(PrintJob job, CancellationToken cancellationToken, bool includeScheduling = true) => throw new NotSupportedException();

    Task<string?> GetBlockingReasonAsync(PrintJob job, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    Task<IReadOnlySet<Guid>> GetEligibleJobIdsAsync(IReadOnlyList<PrintJob> jobs, CancellationToken cancellationToken);
}
