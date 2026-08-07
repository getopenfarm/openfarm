using OpenFarm.Application.Production;

namespace OpenFarm.Api.Endpoints.ProductionRuns;

public sealed class JobStatusSummaryResponse
{
    public int Queued { get; set; }

    public int Printing { get; set; }

    public int Paused { get; set; }

    public int Completed { get; set; }

    public int Failed { get; set; }

    public int Cancelled { get; set; }

    public static JobStatusSummaryResponse FromSummary(JobStatusSummary summary) => new()
    {
        Queued = summary.Queued,
        Printing = summary.Printing,
        Paused = summary.Paused,
        Completed = summary.Completed,
        Failed = summary.Failed,
        Cancelled = summary.Cancelled,
    };
}

public sealed class ProductionRunSummaryResponse
{
    public Guid Id { get; set; }

    public string DisplayLabel { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateOnly? DueDate { get; set; }

    public int LineCount { get; set; }

    public JobStatusSummaryResponse JobSummary { get; set; } = new();

    public static ProductionRunSummaryResponse FromSummary(ProductionRunSummary summary) => new()
    {
        Id = summary.Id,
        DisplayLabel = summary.DisplayLabel,
        Status = summary.Status.ToString(),
        DueDate = summary.DueDate,
        LineCount = summary.LineCount,
        JobSummary = JobStatusSummaryResponse.FromSummary(summary.JobSummary),
    };
}

public sealed class PartRequirementSummaryResponse
{
    public Guid Id { get; set; }

    public Guid PartId { get; set; }

    public string PartName { get; set; } = string.Empty;

    public decimal? Quantity { get; set; }

    public int JobCount { get; set; }

    public int CompletedJobCount { get; set; }

    public List<Guid> JobsNeedingRequeue { get; set; } = [];

    public static PartRequirementSummaryResponse FromSummary(PartRequirementSummary summary) => new()
    {
        Id = summary.Id,
        PartId = summary.PartId,
        PartName = summary.PartName,
        Quantity = summary.Quantity,
        JobCount = summary.JobCount,
        CompletedJobCount = summary.CompletedJobCount,
        JobsNeedingRequeue = summary.JobsNeedingRequeue.ToList(),
    };
}

public sealed class RunLineSummaryResponse
{
    public Guid Id { get; set; }

    public string TargetKind { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public string TargetName { get; set; } = string.Empty;

    public int? Quantity { get; set; }

    public string RevisionMode { get; set; } = string.Empty;

    public List<PartRequirementSummaryResponse> Requirements { get; set; } = [];

    public static RunLineSummaryResponse FromSummary(RunLineSummary summary) => new()
    {
        Id = summary.Id,
        TargetKind = summary.TargetKind.ToString(),
        TargetId = summary.TargetId,
        TargetName = summary.TargetName,
        Quantity = summary.Quantity,
        RevisionMode = summary.RevisionMode.ToString(),
        Requirements = summary.Requirements.Select(PartRequirementSummaryResponse.FromSummary).ToList(),
    };
}

public sealed class ProductionRunDetailResponse
{
    public Guid Id { get; set; }

    public string? Name { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? Priority { get; set; }

    public string? Notes { get; set; }

    public string Status { get; set; } = string.Empty;

    public List<RunLineSummaryResponse> Lines { get; set; } = [];

    public static ProductionRunDetailResponse FromView(RunDetailView view) => new()
    {
        Id = view.Id,
        Name = view.Name,
        DueDate = view.DueDate,
        Priority = view.Priority,
        Notes = view.Notes,
        Status = view.Status.ToString(),
        Lines = view.Lines.Select(RunLineSummaryResponse.FromSummary).ToList(),
    };
}
