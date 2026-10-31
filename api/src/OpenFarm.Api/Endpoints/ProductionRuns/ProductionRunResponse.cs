using OpenFarm.Application.Production;

namespace OpenFarm.Api.Endpoints.ProductionRuns;

/// <summary>Plates by state, as the run's progress strip draws them; a reprinted lost plate is left out.</summary>
public sealed class PlateCountsResponse
{
    public int Completed { get; set; }

    public int Printing { get; set; }

    public int Queued { get; set; }

    /// <summary>Failed or cancelled, awaiting a reprint or write-off.</summary>
    public int NeedsAction { get; set; }

    public int WrittenOff { get; set; }

    public static PlateCountsResponse FromCounts(PlateCounts counts) => new()
    {
        Completed = counts.Completed,
        Printing = counts.Printing,
        Queued = counts.Queued,
        NeedsAction = counts.NeedsAction,
        WrittenOff = counts.WrittenOff,
    };
}

public sealed class RunLineBriefResponse
{
    public string TargetName { get; set; } = string.Empty;

    public int? Quantity { get; set; }
}

public sealed class ProductionRunSummaryResponse
{
    public Guid Id { get; set; }

    public string DisplayLabel { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateOnly? DueDate { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<RunLineBriefResponse> Lines { get; set; } = [];

    /// <summary>Units the finite lines ask for, every printed part counted.</summary>
    public decimal TargetUnits { get; set; }

    /// <summary>Completed units toward the target, at most the target per part.</summary>
    public decimal CompletedUnits { get; set; }

    public int WrittenOffUnits { get; set; }

    /// <summary>Units the unlimited lines have produced; they have no target.</summary>
    public int ProducedUnits { get; set; }

    /// <summary>The finite lines' plates.</summary>
    public PlateCountsResponse Plates { get; set; } = new();

    /// <summary>Waiting jobs whose last send to a printer failed.</summary>
    public int BlockedCount { get; set; }

    public static ProductionRunSummaryResponse FromSummary(ProductionRunSummary summary) => new()
    {
        Id = summary.Id,
        DisplayLabel = summary.DisplayLabel,
        Name = summary.Name,
        Status = summary.Status.ToString(),
        DueDate = summary.DueDate,
        CreatedAtUtc = summary.CreatedAtUtc,
        Lines = summary.Lines.Select(l => new RunLineBriefResponse { TargetName = l.TargetName, Quantity = l.Quantity }).ToList(),
        TargetUnits = summary.TargetUnits,
        CompletedUnits = summary.CompletedUnits,
        WrittenOffUnits = summary.WrittenOffUnits,
        ProducedUnits = summary.ProducedUnits,
        Plates = PlateCountsResponse.FromCounts(summary.Plates),
        BlockedCount = summary.BlockedCount,
    };
}

public sealed class RunJobSummaryResponse
{
    public int? TargetUnits { get; set; }
    public int? ProducedUnits { get; set; }
    public int? MissingUnits { get; set; }
    public bool OutputVerified { get; set; }
    public Guid Id { get; set; }

    public string Status { get; set; } = string.Empty;

    public Guid? PrinterId { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? EndedAtUtc { get; set; }

    /// <summary>Why the last send of this waiting job failed; null otherwise.</summary>
    public string? LastBlockReason { get; set; }

    public Guid? RequeuedAsJobId { get; set; }

    public bool WrittenOff { get; set; }

    /// <summary>A lost plate awaiting a reprint or write-off.</summary>
    public bool NeedsAction { get; set; }

    /// <summary>A lost plate that can be reprinted (it has its snapshot and the run is open); otherwise only a write-off resolves it.</summary>
    public bool CanRequeue { get; set; }

    public static RunJobSummaryResponse FromSummary(RunJobSummary summary) => new()
    {
        Id = summary.Id,
        Status = summary.Status.ToString(),
        PrinterId = summary.PrinterId,
        StartedAtUtc = summary.StartedAtUtc,
        EndedAtUtc = summary.EndedAtUtc,
        LastBlockReason = summary.LastBlockReason,
        RequeuedAsJobId = summary.RequeuedAsJobId,
        WrittenOff = summary.WrittenOff,
        NeedsAction = summary.NeedsAction,
        CanRequeue = summary.CanRequeue,
        TargetUnits = summary.TargetUnits,
        ProducedUnits = summary.ProducedUnits,
        MissingUnits = summary.MissingUnits,
        OutputVerified = summary.OutputVerified,
    };
}

public sealed class PartRequirementSummaryResponse
{
    public Guid Id { get; set; }

    public Guid PartId { get; set; }

    public string PartName { get; set; } = string.Empty;

    public bool SourcePartAvailable { get; set; }

    /// <summary>Target units; null on an unlimited line.</summary>
    public decimal? Quantity { get; set; }

    public int UnitsPerPlate { get; set; }

    public int CompletedUnits { get; set; }

    public int WrittenOffUnits { get; set; }

    public PlateCountsResponse Plates { get; set; } = new();

    public int? EstimatedMinutesPerPlate { get; set; }

    public decimal? EstimatedGramsPerPlate { get; set; }

    /// <summary>Why the next waiting plate cannot go to any printer now; null when one can take it.</summary>
    public string? BlockingReason { get; set; }

    /// <summary>Every plate in queue order; a reprint sits next to the plate it replaces.</summary>
    public List<RunJobSummaryResponse> Jobs { get; set; } = [];

    public static PartRequirementSummaryResponse FromSummary(PartRequirementSummary summary) => new()
    {
        Id = summary.Id,
        PartId = summary.PartId,
        PartName = summary.PartName,
        SourcePartAvailable = summary.SourcePartAvailable,
        Quantity = summary.Quantity,
        UnitsPerPlate = summary.UnitsPerPlate,
        CompletedUnits = summary.CompletedUnits,
        WrittenOffUnits = summary.WrittenOffUnits,
        Plates = PlateCountsResponse.FromCounts(summary.Plates),
        EstimatedMinutesPerPlate = summary.EstimatedMinutesPerPlate,
        EstimatedGramsPerPlate = summary.EstimatedGramsPerPlate,
        BlockingReason = summary.BlockingReason,
        Jobs = summary.Jobs.Select(RunJobSummaryResponse.FromSummary).ToList(),
    };
}

public sealed class RunLineSummaryResponse
{
    public string QuantityBasis { get; set; } = "Units";

    public Guid Id { get; set; }

    public string TargetKind { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public string TargetName { get; set; } = string.Empty;

    public int? Quantity { get; set; }

    public string RevisionMode { get; set; } = string.Empty;

    /// <summary>The printer this line's new jobs are pinned to; null for automatic assignment.</summary>
    public Guid? PinnedPrinterId { get; set; }

    public int BackfillPrinterCap { get; set; }

    public int? BackfillCumulativeUnitCeiling { get; set; }

    public int CumulativeProducedUnits { get; set; }

    public bool SpansMultipleRevisions { get; set; }

    public List<PartRequirementSummaryResponse> Requirements { get; set; } = [];

    public static RunLineSummaryResponse FromSummary(RunLineSummary summary) => new()
    {
        Id = summary.Id,
        TargetKind = summary.TargetKind.ToString(),
        TargetId = summary.TargetId,
        TargetName = summary.TargetName,
        Quantity = summary.Quantity,
        QuantityBasis = summary.QuantityBasis.ToString(),
        RevisionMode = summary.RevisionMode.ToString(),
        PinnedPrinterId = summary.PinnedPrinterId,
        BackfillPrinterCap = summary.BackfillPrinterCap,
        BackfillCumulativeUnitCeiling = summary.BackfillCumulativeUnitCeiling,
        CumulativeProducedUnits = summary.CumulativeProducedUnits,
        SpansMultipleRevisions = summary.SpansMultipleRevisions,
        Requirements = summary.Requirements.Select(PartRequirementSummaryResponse.FromSummary).ToList(),
    };
}

public sealed class ProductionRunDetailResponse
{
    public Guid Id { get; set; }

    public string DisplayLabel { get; set; } = string.Empty;

    public string? Name { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? Notes { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<RunLineSummaryResponse> Lines { get; set; } = [];

    public static ProductionRunDetailResponse FromView(RunDetailView view) => new()
    {
        Id = view.Id,
        DisplayLabel = view.DisplayLabel,
        Name = view.Name,
        DueDate = view.DueDate,
        Notes = view.Notes,
        Status = view.Status.ToString(),
        CreatedAtUtc = view.CreatedAtUtc,
        Lines = view.Lines.Select(RunLineSummaryResponse.FromSummary).ToList(),
    };
}
