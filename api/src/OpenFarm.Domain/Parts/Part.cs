namespace OpenFarm.Domain.Parts;

/// <summary>
/// A catalog part — always exactly one File (never a collection). By construction, every row has a
/// fully resolved <see cref="NozzleDiameterMm"/> and filament requirement(s) — <see cref="EstimateStatus"/>
/// is the one legitimate way a row can carry incomplete derived data (data-model.md).
/// </summary>
public sealed class Part
{
    private readonly List<PartFilamentRequirement> _filamentRequirements = [];
    private readonly List<PartThumbnail> _thumbnails = [];
    private readonly List<Tag> _tags = [];

    private Part(
        Guid id,
        Guid organizationId,
        string name,
        PartFileFormat fileFormat,
        bool requiresSlicing,
        string originalFileName,
        string storagePath,
        long fileSizeBytes,
        decimal nozzleDiameterMm,
        SlicerEngine? slicerEngineOverride,
        string? slicerVersionUsed,
        EstimateStatus estimateStatus,
        int? estimatedPrintTimeMinutes,
        decimal? estimatedFilamentWeightGrams,
        int plateCount,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        FileFormat = fileFormat;
        RequiresSlicing = requiresSlicing;
        OriginalFileName = originalFileName;
        StoragePath = storagePath;
        FileSizeBytes = fileSizeBytes;
        FileUploadedAtUtc = createdAtUtc;
        NozzleDiameterMm = nozzleDiameterMm;
        SlicerEngineOverride = slicerEngineOverride;
        SlicerVersionUsed = slicerVersionUsed;
        EstimateStatus = estimateStatus;
        EstimatedPrintTimeMinutes = estimatedPrintTimeMinutes;
        EstimatedFilamentWeightGrams = estimatedFilamentWeightGrams;
        PlateCount = plateCount;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public Guid OrganizationId { get; }

    public string Name { get; private set; }

    public PartFileFormat FileFormat { get; private set; }

    public bool RequiresSlicing { get; private set; }

    public string OriginalFileName { get; private set; }

    public string StoragePath { get; private set; }

    public long FileSizeBytes { get; private set; }

    public DateTimeOffset FileUploadedAtUtc { get; private set; }

    public decimal NozzleDiameterMm { get; private set; }

    public EstimateStatus EstimateStatus { get; private set; }

    public int? EstimatedPrintTimeMinutes { get; private set; }

    public decimal? EstimatedFilamentWeightGrams { get; private set; }

    public SlicerEngine? SlicerEngineOverride { get; private set; }

    /// <summary>The exact upstream version actually produced with (FR-005) — <c>null</c> for a non-slicing file or a legacy pre-feature row.</summary>
    public string? SlicerVersionUsed { get; private set; }

    public int PlateCount { get; private set; }

    public IReadOnlyList<PartFilamentRequirement> FilamentRequirements => _filamentRequirements;

    public IReadOnlyList<PartThumbnail> Thumbnails => _thumbnails;

    public IReadOnlyList<Tag> Tags => _tags;

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Part Create(
        Guid organizationId,
        string name,
        PartFileFormat fileFormat,
        bool requiresSlicing,
        string originalFileName,
        string storagePath,
        long fileSizeBytes,
        decimal nozzleDiameterMm,
        SlicerEngine? slicerEngineOverride,
        string? slicerVersionUsed,
        EstimateStatus estimateStatus,
        int? estimatedPrintTimeMinutes,
        decimal? estimatedFilamentWeightGrams,
        int plateCount,
        IReadOnlyList<FilamentRequirementSpec> filamentRequirements,
        IReadOnlyList<ThumbnailSpec> thumbnails,
        IReadOnlyList<Tag> tags)
    {
        ValidateSharedFields(name, originalFileName, storagePath, fileSizeBytes, nozzleDiameterMm, plateCount, filamentRequirements);

        var part = new Part(
            Guid.NewGuid(),
            organizationId,
            name,
            fileFormat,
            requiresSlicing,
            originalFileName,
            storagePath,
            fileSizeBytes,
            nozzleDiameterMm,
            slicerEngineOverride,
            slicerVersionUsed,
            estimateStatus,
            estimatedPrintTimeMinutes,
            estimatedFilamentWeightGrams,
            plateCount,
            DateTimeOffset.UtcNow);

        foreach (var spec in filamentRequirements)
        {
            part._filamentRequirements.Add(PartFilamentRequirement.Create(organizationId, part.Id, spec));
        }

        foreach (var thumbnail in thumbnails)
        {
            part._thumbnails.Add(PartThumbnail.Create(organizationId, part.Id, thumbnail.PlateIndex, thumbnail.Bytes));
        }

        foreach (var tag in tags)
        {
            part._tags.Add(tag);
        }

        return part;
    }

    /// <summary>
    /// A direct, all-or-nothing replacement (never additive) — keeps <see cref="Name"/>/<see cref="Tags"/>
    /// untouched, replaces every file-derived field wholesale (data-model.md, FR-004).
    /// </summary>
    public void ReplaceFile(
        PartFileFormat fileFormat,
        bool requiresSlicing,
        string originalFileName,
        string storagePath,
        long fileSizeBytes,
        decimal nozzleDiameterMm,
        SlicerEngine? slicerEngineOverride,
        string? slicerVersionUsed,
        EstimateStatus estimateStatus,
        int? estimatedPrintTimeMinutes,
        decimal? estimatedFilamentWeightGrams,
        int plateCount,
        IReadOnlyList<FilamentRequirementSpec> filamentRequirements,
        IReadOnlyList<ThumbnailSpec> thumbnails)
    {
        ValidateSharedFields(Name, originalFileName, storagePath, fileSizeBytes, nozzleDiameterMm, plateCount, filamentRequirements);

        FileFormat = fileFormat;
        RequiresSlicing = requiresSlicing;
        OriginalFileName = originalFileName;
        StoragePath = storagePath;
        FileSizeBytes = fileSizeBytes;
        FileUploadedAtUtc = DateTimeOffset.UtcNow;
        NozzleDiameterMm = nozzleDiameterMm;
        SlicerEngineOverride = slicerEngineOverride;
        SlicerVersionUsed = slicerVersionUsed;
        EstimateStatus = estimateStatus;
        EstimatedPrintTimeMinutes = estimatedPrintTimeMinutes;
        EstimatedFilamentWeightGrams = estimatedFilamentWeightGrams;
        PlateCount = plateCount;

        _filamentRequirements.Clear();
        foreach (var spec in filamentRequirements)
        {
            _filamentRequirements.Add(PartFilamentRequirement.Create(OrganizationId, Id, spec));
        }

        _thumbnails.Clear();
        foreach (var thumbnail in thumbnails)
        {
            _thumbnails.Add(PartThumbnail.Create(OrganizationId, Id, thumbnail.PlateIndex, thumbnail.Bytes));
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// The research.md §3b trailing continuation's success path — targets a still-<c>Resolving</c>
    /// part confirmed early (FR-009c/FR-009d). A no-op if the estimate has already resolved/failed by
    /// the time this runs (idempotency guard against a redundant continuation firing twice).
    /// </summary>
    public void ResolveEstimate(int estimatedPrintTimeMinutes, decimal estimatedFilamentWeightGrams, IReadOnlyList<ThumbnailSpec> thumbnails)
    {
        if (EstimateStatus != EstimateStatus.Resolving)
        {
            return;
        }

        EstimateStatus = EstimateStatus.Available;
        EstimatedPrintTimeMinutes = estimatedPrintTimeMinutes;
        EstimatedFilamentWeightGrams = estimatedFilamentWeightGrams;

        foreach (var thumbnail in thumbnails)
        {
            _thumbnails.Add(PartThumbnail.Create(OrganizationId, Id, thumbnail.PlateIndex, thumbnail.Bytes));
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>The research.md §3b trailing continuation's failure path — permanent, no retry in this phase (FR-009d).</summary>
    public void MarkEstimateUnavailable()
    {
        if (EstimateStatus != EstimateStatus.Resolving)
        {
            return;
        }

        EstimateStatus = EstimateStatus.Unavailable;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void AddTag(Tag tag)
    {
        if (tag.OrganizationId != OrganizationId)
        {
            throw new ArgumentException("Tag belongs to a different organization.", nameof(tag));
        }

        if (_tags.Any(t => t.Id == tag.Id))
        {
            throw new DuplicatePartTagException(tag.Name);
        }

        _tags.Add(tag);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void RemoveTag(Guid tagId)
    {
        if (_tags.RemoveAll(t => t.Id == tagId) > 0)
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>FR-012 (US4) — standalone narrow/change/clear of one filament requirement's choice, reachable at any time after confirm.</summary>
    public PartFilamentRequirement NarrowFilamentRequirement(Guid requirementId, Guid? narrowFilamentVariantId, Guid? narrowFilamentProfileId)
    {
        var requirement = _filamentRequirements.FirstOrDefault(r => r.Id == requirementId)
            ?? throw new PartFilamentRequirementNotFoundException(Id, requirementId);

        requirement.Narrow(narrowFilamentVariantId, narrowFilamentProfileId);
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        return requirement;
    }

    private static void ValidateSharedFields(
        string name,
        string originalFileName,
        string storagePath,
        long fileSizeBytes,
        decimal nozzleDiameterMm,
        int plateCount,
        IReadOnlyList<FilamentRequirementSpec> filamentRequirements)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Part name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new ArgumentException("Original file name is required.", nameof(originalFileName));
        }

        if (string.IsNullOrWhiteSpace(storagePath))
        {
            throw new ArgumentException("Storage path is required.", nameof(storagePath));
        }

        if (fileSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSizeBytes), "File size cannot be negative.");
        }

        if (nozzleDiameterMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nozzleDiameterMm), "Nozzle diameter must be positive.");
        }

        if (plateCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(plateCount), "Plate count must be at least 1.");
        }

        if (filamentRequirements.Count == 0)
        {
            throw new ArgumentException("A part must have at least one filament requirement.", nameof(filamentRequirements));
        }
    }
}
