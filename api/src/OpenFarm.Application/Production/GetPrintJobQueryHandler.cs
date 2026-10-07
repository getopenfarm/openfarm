using OpenFarm.Application.Common;

namespace OpenFarm.Application.Production;

public sealed class GetPrintJobQueryHandler(IPrintJobRepository jobRepository) : IQueryHandler<GetPrintJobQuery, PrintJobDetail?>
{
    public async Task<PrintJobDetail?> HandleAsync(GetPrintJobQuery query, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(query.JobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        return new PrintJobDetail(
            job.Id, job.PartRequirementId, job.QueuePosition, job.PlateQuantity, job.AssignmentMode,
            job.PinnedPrinterId, job.DispatchedPrinterId, job.Status, job.FileRevisionHash, job.FileRevisionNumber,
            job.DispatchedAtUtc, job.StartedAtUtc, job.EndedAtUtc, job.ActualDurationMinutes,
            job.CancelRequestedAtUtc, job.RequeuedAsJobId,
            job.FilamentConsumption
                .Select(c => new JobFilamentConsumptionDetail(c.Id, c.MaterialType, c.DetectedColorHex, c.SpoolId, c.GramsConsumed, c.NarrowFilamentVariantId, c.NarrowFilamentProfileId))
                .ToList(),
            job.CreatedAtUtc);
    }
}
