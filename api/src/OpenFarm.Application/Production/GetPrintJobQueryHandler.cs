using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed class GetPrintJobQueryHandler(IPrintJobRepository jobRepository, IExternalPrintRepository? externalPrintRepository = null, IPartRequirementRepository? requirements = null, IProductionRunRepository? runs = null) : IQueryHandler<GetPrintJobQuery, PrintJobDetail?>
{
    public async Task<PrintJobDetail?> HandleAsync(GetPrintJobQuery query, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(query.JobId, cancellationToken);
        if (job is null)
        {
            return externalPrintRepository is not null && await externalPrintRepository.GetByIdAsync(query.JobId, cancellationToken) is { } external
                ? ExternalDetail(external)
                : null;
        }

        var requirement = requirements is null ? null : await requirements.GetByIdAsync(job.PartRequirementId, cancellationToken);
        var run = requirement is null || runs is null ? null : await runs.GetByRunLineIdAsync(requirement.RunLineId, cancellationToken);
        var canResolve = requirement?.Quantity is not null && job.IsUnresolvedLoss && (run?.IsOpen ?? true);
        return new PrintJobDetail(
            job.Id, job.PartRequirementId, job.QueuePosition, job.PlateQuantity, job.AssignmentMode,
            job.PinnedPrinterId, job.PrinterId, job.Status, job.FileRevisionHash, job.FileRevisionNumber,
            job.DispatchedAtUtc, job.StartedAtUtc, job.EndedAtUtc, job.ActualDurationMinutes,
            job.CancelRequestedAtUtc, job.RequeuedAsJobId,
            job.Filaments
                .Select(c => new JobFilamentConsumptionDetail(c.Id, c.MaterialType, c.DetectedColorHex, c.SpoolId, c.GramsConsumed, c.NarrowFilamentVariantId, c.NarrowFilamentProfileId, c.ToolIndex, c.EstimatedGrams, c.PricePerKg, c.PriceSource, c.Cost))
                .ToList(),
            job.CreatedAtUtc, job.GetSnapshotState(), job.SnapshotRecoveryReason, job.Source, job.Name,
            job.LastBlockReason, job.LastBlockedAtUtc, DispatchProvenance.FromJson(job.DispatchProvenanceJson), job.WrittenOffAtUtc, job.EffectiveTargetUnits, job.ProducedUnits, job.MissingUnits, canResolve);
    }

    /// <summary>A print the printer started itself has no requirement, queue slot, plate or file revision.</summary>
    private static PrintJobDetail ExternalDetail(ExternalPrint print) => new(
        print.Id, null, null, null, null, null, print.PrinterId, print.Status, null, null, null,
        print.StartedAtUtc, print.EndedAtUtc, print.ActualDurationMinutes, null, null,
        print.Filaments.OrderBy(f => f.ToolIndex)
            .Select(f => new JobFilamentConsumptionDetail(f.Id, f.MaterialType, f.DetectedColorHex, f.SpoolId, f.GramsConsumed, null, null, f.ToolIndex, f.EstimatedGrams, f.PricePerKg, f.PriceSource, f.Cost))
            .ToList(),
        print.CreatedAtUtc, "Ready", null, print.Source, print.Name);
}
