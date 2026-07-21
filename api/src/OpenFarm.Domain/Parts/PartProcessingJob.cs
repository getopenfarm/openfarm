namespace OpenFarm.Domain.Parts;

/// <summary>
/// Tracks one add/replace's Processing → Ready for Review → Confirm/Discard lifecycle, uniformly for
/// every file type (data-model.md, research.md §2/§3). A job never restarts — a discarded or failed
/// add/replace requires a brand-new request.
/// </summary>
public sealed class PartProcessingJob
{
    private readonly List<PartProcessingJobPreviewThumbnail> _previewThumbnails = [];
    private IReadOnlyList<PreviewFilamentRequirement> _previewFilamentRequirements = [];

    private PartProcessingJob(
        Guid id,
        Guid organizationId,
        Guid? partId,
        PartProcessingJobKind kind,
        PartFileFormat fileFormat,
        string originalFileName,
        string storagePath,
        long fileSizeBytes,
        bool requiresSlicing,
        SlicerEngine? slicerEngineUsed,
        string? slicerVersionUsed,
        decimal? targetNozzleDiameterMm,
        string? targetMaterialType,
        Guid? requestedByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        PartId = partId;
        Kind = kind;
        Status = PartProcessingJobStatus.Processing;
        FileFormat = fileFormat;
        OriginalFileName = originalFileName;
        StoragePath = storagePath;
        FileSizeBytes = fileSizeBytes;
        RequiresSlicing = requiresSlicing;
        SlicerEngineUsed = slicerEngineUsed;
        SlicerVersionUsed = slicerVersionUsed;
        TargetNozzleDiameterMm = targetNozzleDiameterMm;
        TargetMaterialType = targetMaterialType;
        RequestedByUserId = requestedByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public Guid? PartId { get; private set; }

    public PartProcessingJobKind Kind { get; }

    public PartProcessingJobStatus Status { get; private set; }

    /// <summary>Snapshot of the submitted file — carried forward so Confirm can build/replace the <see cref="Part"/> without re-reading the upload (implementation detail, not itself part of the reviewable preview).</summary>
    public PartFileFormat FileFormat { get; }

    public string OriginalFileName { get; }

    public string StoragePath { get; private set; }

    public long FileSizeBytes { get; private set; }

    public bool RequiresSlicing { get; }

    public SlicerEngine? SlicerEngineUsed { get; }

    /// <summary>The exact upstream version resolved alongside <see cref="SlicerEngineUsed"/> at job creation (FR-005) — <c>null</c> for a non-slicing job.</summary>
    public string? SlicerVersionUsed { get; }

    public decimal? TargetNozzleDiameterMm { get; }

    /// <summary>
    /// The concrete slicing target — for an STL, known at job creation (the fixed fallback profile);
    /// for an unsliced 3MF, unknown until milestone 1 parses the project's own settings, at which
    /// point <see cref="MarkReadyForReview"/> updates it (research.md §13). Always <c>null</c> for an
    /// already-sliced file, which never re-slices at all.
    /// </summary>
    public string? TargetMaterialType { get; private set; }

    public decimal? PreviewNozzleDiameterMm { get; private set; }

    public EstimateStatus? PreviewEstimateStatus { get; private set; }

    public int? PreviewEstimatedPrintTimeMinutes { get; private set; }

    public decimal? PreviewEstimatedFilamentWeightGrams { get; private set; }

    public int? PreviewPlateCount { get; private set; }

    public IReadOnlyList<PartProcessingJobPreviewThumbnail> PreviewThumbnails => _previewThumbnails;

    public IReadOnlyList<PreviewFilamentRequirement> PreviewFilamentRequirements => _previewFilamentRequirements;

    public string? ErrorMessage { get; private set; }

    public Guid? RequestedByUserId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public static PartProcessingJob CreateAdd(
        Guid organizationId,
        PartFileFormat fileFormat,
        string originalFileName,
        string storagePath,
        long fileSizeBytes,
        bool requiresSlicing,
        SlicerEngine? slicerEngineUsed,
        string? slicerVersionUsed,
        decimal? targetNozzleDiameterMm,
        string? targetMaterialType,
        Guid? requestedByUserId) =>
        new(Guid.NewGuid(), organizationId, null, PartProcessingJobKind.Add, fileFormat, originalFileName, storagePath, fileSizeBytes, requiresSlicing, slicerEngineUsed, slicerVersionUsed, targetNozzleDiameterMm, targetMaterialType, requestedByUserId, DateTimeOffset.UtcNow);

    public static PartProcessingJob CreateReplace(
        Guid organizationId,
        Guid partId,
        PartFileFormat fileFormat,
        string originalFileName,
        string storagePath,
        long fileSizeBytes,
        bool requiresSlicing,
        SlicerEngine? slicerEngineUsed,
        string? slicerVersionUsed,
        decimal? targetNozzleDiameterMm,
        string? targetMaterialType,
        Guid? requestedByUserId) =>
        new(Guid.NewGuid(), organizationId, partId, PartProcessingJobKind.Replace, fileFormat, originalFileName, storagePath, fileSizeBytes, requiresSlicing, slicerEngineUsed, slicerVersionUsed, targetNozzleDiameterMm, targetMaterialType, requestedByUserId, DateTimeOffset.UtcNow);

    /// <summary>
    /// The job's own id is the storage key an Add's target file is saved under (its target
    /// <see cref="Part"/> doesn't exist yet) — called once, immediately after
    /// <see cref="IPartFileStorage"/> persists the upload, since the id is only known post-construction.
    /// </summary>
    public void SetStoragePath(string storagePath, long fileSizeBytes)
    {
        StoragePath = storagePath;
        FileSizeBytes = fileSizeBytes;
    }

    /// <summary>
    /// Milestone 1 (research.md §3b) — nozzle diameter/material (and, for an already-sliced file,
    /// everything else too) resolved. <paramref name="estimateStatus"/> is <c>Available</c>
    /// immediately for a file that doesn't require slicing, <c>Resolving</c> for one that does (its
    /// own trailing continuation completes it later via <see cref="ResolveEstimate"/>).
    /// </summary>
    public void MarkReadyForReview(
        decimal nozzleDiameterMm,
        IReadOnlyList<PreviewFilamentRequirement> filamentRequirements,
        int plateCount,
        EstimateStatus estimateStatus,
        int? estimatedPrintTimeMinutes,
        decimal? estimatedFilamentWeightGrams,
        IReadOnlyList<ThumbnailSpec> thumbnails,
        string? resolvedTargetMaterialType = null)
    {
        EnsureProcessing();

        if (estimateStatus == EstimateStatus.Unavailable)
        {
            throw new ArgumentException("A job's preview estimate status is never Unavailable — only a confirmed Part's can be.", nameof(estimateStatus));
        }

        Status = PartProcessingJobStatus.ReadyForReview;
        TargetMaterialType = resolvedTargetMaterialType ?? TargetMaterialType;
        PreviewNozzleDiameterMm = nozzleDiameterMm;
        _previewFilamentRequirements = filamentRequirements;
        PreviewPlateCount = plateCount;
        PreviewEstimateStatus = estimateStatus;
        PreviewEstimatedPrintTimeMinutes = estimatedPrintTimeMinutes;
        PreviewEstimatedFilamentWeightGrams = estimatedFilamentWeightGrams;

        foreach (var thumbnail in thumbnails)
        {
            _previewThumbnails.Add(PartProcessingJobPreviewThumbnail.Create(OrganizationId, Id, thumbnail.PlateIndex, thumbnail.Bytes));
        }

        ProcessedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// research.md §3b's trailing continuation, success case, while still unconfirmed — refreshes the
    /// preview in place; <see cref="Status"/> stays <c>ReadyForReview</c>.
    /// </summary>
    public void ResolveEstimate(int estimatedPrintTimeMinutes, decimal estimatedFilamentWeightGrams, IReadOnlyList<ThumbnailSpec> thumbnails)
    {
        if (Status != PartProcessingJobStatus.ReadyForReview || PreviewEstimateStatus != EstimateStatus.Resolving)
        {
            return;
        }

        PreviewEstimateStatus = EstimateStatus.Available;
        PreviewEstimatedPrintTimeMinutes = estimatedPrintTimeMinutes;
        PreviewEstimatedFilamentWeightGrams = estimatedFilamentWeightGrams;

        foreach (var thumbnail in thumbnails)
        {
            _previewThumbnails.Add(PartProcessingJobPreviewThumbnail.Create(OrganizationId, Id, thumbnail.PlateIndex, thumbnail.Bytes));
        }
    }

    /// <summary>
    /// Reachable from <c>Processing</c> (nozzle diameter/material itself couldn't be resolved) or,
    /// retroactively, from <c>ReadyForReview</c> (research.md §3's new transition — a still-unconfirmed
    /// job's trailing slice subsequently fails). Discards any preview already shown (contracts/
    /// part-library-api.md).
    /// </summary>
    public void MarkFailed(string errorMessage)
    {
        if (Status is not (PartProcessingJobStatus.Processing or PartProcessingJobStatus.ReadyForReview))
        {
            throw new PartProcessingJobNotReadyForReviewException(Id);
        }

        Status = PartProcessingJobStatus.Failed;
        ErrorMessage = errorMessage;
        PreviewNozzleDiameterMm = null;
        _previewFilamentRequirements = [];
        PreviewPlateCount = null;
        PreviewEstimateStatus = null;
        PreviewEstimatedPrintTimeMinutes = null;
        PreviewEstimatedFilamentWeightGrams = null;
        _previewThumbnails.Clear();
        ProcessedAtUtc ??= DateTimeOffset.UtcNow;
    }

    /// <summary>The Save action — <paramref name="partId"/> is the newly-created Part's id for an Add, or the already-known target for a Replace.</summary>
    public void Confirm(Guid partId)
    {
        EnsureReadyForReview();
        Status = PartProcessingJobStatus.Confirmed;
        PartId = partId;
        ResolvedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>The Cancel action — valid while <c>Processing</c> or once <c>ReadyForReview</c> (contracts/part-library-api.md).</summary>
    public void Discard()
    {
        if (Status is not (PartProcessingJobStatus.Processing or PartProcessingJobStatus.ReadyForReview))
        {
            throw new PartProcessingJobNotReadyForReviewException(Id);
        }

        Status = PartProcessingJobStatus.Discarded;
        ResolvedAtUtc = DateTimeOffset.UtcNow;
    }

    private void EnsureProcessing()
    {
        if (Status != PartProcessingJobStatus.Processing)
        {
            throw new InvalidOperationException($"Part processing job '{Id}' is not currently Processing.");
        }
    }

    private void EnsureReadyForReview()
    {
        if (Status != PartProcessingJobStatus.ReadyForReview)
        {
            throw new PartProcessingJobNotReadyForReviewException(Id);
        }
    }
}
