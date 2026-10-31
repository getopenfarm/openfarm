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

    /// <summary>Why the last attempt to send this queued job failed after a printer matched it (slicing, feed mapping, send); <c>null</c> when none did.</summary>
    public string? LastBlockReason { get; set; }

    public Guid? PartId { get; set; }

    public string PartName { get; set; } = string.Empty;

    public int? TargetUnits { get; set; }
    public int? ProducedUnits { get; set; }
    public int? MissingUnits { get; set; }
    /// <summary>The part's first plate preview as base64 PNG; null when the part has none.</summary>
    public string? ThumbnailBase64 { get; set; }
    public int PlateQuantity { get; set; }

    public Guid? RunLineId { get; set; }
    public Guid? DemandId { get; set; }
    public string? DemandMode { get; set; }
    public bool CanRequeue { get; set; }

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
        LastBlockReason = row.LastBlockReason,
        PartId = row.PartId,
        PartName = row.PartName,
        PlateQuantity = row.PlateQuantity,
        TargetUnits = row.TargetUnits,
        ProducedUnits = row.ProducedUnits,
        MissingUnits = row.MissingUnits,
        ThumbnailBase64 = row.ThumbnailBase64,
        RunLineId = row.RunLineId,
        DemandId = row.RunLineId,
        DemandMode = row.DemandMode,
        CanRequeue = row.CanRequeue,
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

    /// <summary>The price per kg the line was costed at, written when it was first deducted; null while no price was known.</summary>
    public decimal? PricePerKg { get; set; }

    /// <summary><c>Spool</c>, <c>Variant</c> (the colour) or <c>Filament</c>: where <see cref="PricePerKg"/> came from.</summary>
    public string? PriceSource { get; set; }

    /// <summary>The grams deducted at that price, unrounded; null without a price or without grams. Round only for display.</summary>
    public decimal? Cost { get; set; }

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
        PricePerKg = detail.PricePerKg,
        PriceSource = detail.PriceSource?.ToString(),
        Cost = detail.Cost,
    };
}

public sealed class PrintJobResponse
{
    public bool CanResolveShortfall { get; set; }
    public Guid Id { get; set; }

    public Guid? PartRequirementId { get; set; }

    public double? QueuePosition { get; set; }

    public int? TargetUnits { get; set; }
    public int? ProducedUnits { get; set; }
    public int? MissingUnits { get; set; }
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

    /// <summary>Set when this lost plate was accepted instead of reprinted.</summary>
    public DateTimeOffset? WrittenOffAtUtc { get; set; }

    public List<JobFilamentConsumptionResponse> FilamentConsumption { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; }
    public string SnapshotState { get; set; } = string.Empty;
    public string? SnapshotRecoveryReason { get; set; }

    /// <summary>"Dispatched" or "External"; an external print has no requirement, queue slot, plate or file revision.</summary>
    public string Source { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Why the last attempt to send this job failed after a printer matched it; <c>null</c> when none did or a later send succeeded.</summary>
    public string? LastBlockReason { get; set; }

    public DateTimeOffset? LastBlockedAtUtc { get; set; }

    /// <summary>The slicer pin and the presets the file sent to the printer was sliced with; <c>null</c> for a file sent as stored or a job never sent.</summary>
    public DispatchProvenanceResponse? Provenance { get; set; }

    public static PrintJobResponse FromDetail(PrintJobDetail detail) => new()
    {
        Id = detail.Id,
        PartRequirementId = detail.PartRequirementId,
        QueuePosition = detail.QueuePosition,
        PlateQuantity = detail.PlateQuantity,
        TargetUnits = detail.TargetUnits,
        ProducedUnits = detail.ProducedUnits,
        MissingUnits = detail.MissingUnits,
        CanResolveShortfall = detail.CanResolveShortfall,
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
        WrittenOffAtUtc = detail.WrittenOffAtUtc,
        FilamentConsumption = detail.FilamentConsumption.Select(JobFilamentConsumptionResponse.FromDetail).ToList(),
        CreatedAtUtc = detail.CreatedAtUtc,
        SnapshotState = detail.SnapshotState,
        SnapshotRecoveryReason = detail.SnapshotRecoveryReason,
        Source = detail.Source.ToString(),
        Name = detail.Name,
        LastBlockReason = detail.LastBlockReason,
        LastBlockedAtUtc = detail.LastBlockedAtUtc,
        Provenance = detail.Provenance is null ? null : DispatchProvenanceResponse.From(detail.Provenance),
    };
}

public sealed class DispatchProvenanceResponse
{
    public string Engine { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public DateTimeOffset AtUtc { get; set; }

    public List<ResolvedPresetResponse> Presets { get; set; } = [];

    public static DispatchProvenanceResponse From(DispatchProvenance provenance) => new()
    {
        Engine = provenance.Pin.Engine.ToString(),
        Version = provenance.Pin.Version,
        AtUtc = provenance.AtUtc,
        Presets = provenance.Presets.Select(p => new ResolvedPresetResponse
        {
            Kind = p.Kind.ToString(),
            SelectedName = p.SelectedName,
            ResolvedName = p.ResolvedName,
            SettingId = p.SettingId,
            SourceVersion = p.SourceVersion,
            Outcome = p.Outcome.ToString(),
        }).ToList(),
    };
}

public sealed class ResolvedPresetResponse
{
    public string Kind { get; set; } = string.Empty;

    public string SelectedName { get; set; } = string.Empty;

    public string ResolvedName { get; set; } = string.Empty;

    public string? SettingId { get; set; }

    public string? SourceVersion { get; set; }

    /// <summary>How the selected preset reached the pinned slicer's own copy: Exact, MappedVersion, MappedEngine or UserNative.</summary>
    public string Outcome { get; set; } = string.Empty;
}
