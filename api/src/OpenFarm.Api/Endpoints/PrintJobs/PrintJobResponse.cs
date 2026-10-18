using OpenFarm.Application.Production;

namespace OpenFarm.Api.Endpoints.PrintJobs;

public sealed class QueueJobRowResponse
{
    public Guid JobId { get; set; }

    public double? QueuePosition { get; set; }

    public string Status { get; set; } = string.Empty;

    public string AssignmentMode { get; set; } = string.Empty;

    public Guid? PinnedPrinterId { get; set; }

    public Guid? DispatchedPrinterId { get; set; }

    public bool IsBlocked { get; set; }
    public string? BlockingReason { get; set; }
    public bool SourcePartAvailable { get; set; }
    public string SnapshotState { get; set; } = string.Empty;
    public string? SnapshotRecoveryReason { get; set; }

    public Guid? PartId { get; set; }

    public string PartName { get; set; } = string.Empty;

    public int PlateQuantity { get; set; }

    public Guid? RunLineId { get; set; }

    public Guid? ProductionRunId { get; set; }

    public string RunDisplayLabel { get; set; } = string.Empty;

    /// <summary>"Dispatched" for a job OpenFarm queued, "External" for a print the printer started itself.</summary>
    public string Source { get; set; } = string.Empty;

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? EndedAtUtc { get; set; }

    public static QueueJobRowResponse FromRow(QueueJobRow row) => new()
    {
        JobId = row.JobId,
        QueuePosition = row.QueuePosition,
        Status = row.Status.ToString(),
        AssignmentMode = row.AssignmentMode.ToString(),
        PinnedPrinterId = row.PinnedPrinterId,
        DispatchedPrinterId = row.DispatchedPrinterId,
        IsBlocked = row.IsBlocked,
        BlockingReason = row.BlockingReason,
        SourcePartAvailable = row.SourcePartAvailable,
        SnapshotState = row.SnapshotState,
        SnapshotRecoveryReason = row.SnapshotRecoveryReason,
        PartId = row.PartId,
        PartName = row.PartName,
        PlateQuantity = row.PlateQuantity,
        RunLineId = row.RunLineId,
        ProductionRunId = row.ProductionRunId,
        RunDisplayLabel = row.RunDisplayLabel,
        Source = row.Source.ToString(),
        StartedAtUtc = row.StartedAtUtc,
        EndedAtUtc = row.EndedAtUtc,
    };
}

public sealed class JobFilamentConsumptionResponse
{
    public Guid Id { get; set; }

    public string? MaterialType { get; set; }

    public string? DetectedColorHex { get; set; }

    public Guid? NarrowFilamentVariantId { get; set; }
    public Guid? NarrowFilamentProfileId { get; set; }
    public Guid? SpoolId { get; set; }

    public decimal? GramsConsumed { get; set; }

    public decimal? EstimatedGrams { get; set; }

    public int ToolIndex { get; set; }

    public static JobFilamentConsumptionResponse FromDetail(JobFilamentConsumptionDetail detail) => new()
    {
        Id = detail.Id,
        MaterialType = detail.MaterialType,
        DetectedColorHex = detail.DetectedColorHex,
        NarrowFilamentVariantId = detail.NarrowFilamentVariantId,
        NarrowFilamentProfileId = detail.NarrowFilamentProfileId,
        SpoolId = detail.SpoolId,
        GramsConsumed = detail.GramsConsumed,
        EstimatedGrams = detail.EstimatedGrams,
        ToolIndex = detail.ToolIndex,
    };
}

public sealed class PrintJobResponse
{
    public Guid Id { get; set; }

    public Guid? PartRequirementId { get; set; }

    public double? QueuePosition { get; set; }

    public int? PlateQuantity { get; set; }

    public string? AssignmentMode { get; set; }

    public Guid? PinnedPrinterId { get; set; }

    public Guid? DispatchedPrinterId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? FileRevisionHash { get; set; }

    public int? FileRevisionNumber { get; set; }

    public DateTimeOffset? DispatchedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? EndedAtUtc { get; set; }

    public int? ActualDurationMinutes { get; set; }

    public DateTimeOffset? CancelRequestedAtUtc { get; set; }

    public Guid? RequeuedAsJobId { get; set; }

    public List<JobFilamentConsumptionResponse> FilamentConsumption { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; }
    public string SnapshotState { get; set; } = string.Empty;
    public string? SnapshotRecoveryReason { get; set; }

    /// <summary>"Dispatched" or "External"; an external print has no requirement, queue slot, plate or file revision.</summary>
    public string Source { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public static PrintJobResponse FromDetail(PrintJobDetail detail) => new()
    {
        Id = detail.Id,
        PartRequirementId = detail.PartRequirementId,
        QueuePosition = detail.QueuePosition,
        PlateQuantity = detail.PlateQuantity,
        AssignmentMode = detail.AssignmentMode?.ToString(),
        PinnedPrinterId = detail.PinnedPrinterId,
        DispatchedPrinterId = detail.DispatchedPrinterId,
        Status = detail.Status.ToString(),
        FileRevisionHash = detail.FileRevisionHash,
        FileRevisionNumber = detail.FileRevisionNumber,
        DispatchedAtUtc = detail.DispatchedAtUtc,
        StartedAtUtc = detail.StartedAtUtc,
        EndedAtUtc = detail.EndedAtUtc,
        ActualDurationMinutes = detail.ActualDurationMinutes,
        CancelRequestedAtUtc = detail.CancelRequestedAtUtc,
        RequeuedAsJobId = detail.RequeuedAsJobId,
        FilamentConsumption = detail.FilamentConsumption.Select(JobFilamentConsumptionResponse.FromDetail).ToList(),
        CreatedAtUtc = detail.CreatedAtUtc,
        SnapshotState = detail.SnapshotState,
        SnapshotRecoveryReason = detail.SnapshotRecoveryReason,
        Source = detail.Source.ToString(),
        Name = detail.Name,
    };
}
