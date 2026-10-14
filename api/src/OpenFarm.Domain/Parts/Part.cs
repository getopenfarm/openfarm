namespace OpenFarm.Domain.Parts;

/// <summary>
/// A catalog part with one stored file. Print metadata and estimates may be incomplete;
/// production validates <see cref="PrintRequirementsStatus"/> separately from library use.
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
        decimal? nozzleDiameterMm,
        SlicerEngine? slicerEngineOverride,
        string? slicerVersionUsed,
        EstimateStatus estimateStatus,
        int? estimatedPrintTimeMinutes,
        decimal? estimatedFilamentWeightGrams,
        int plateCount,
        int unitsPerPrint,
        string revisionHash,
        int revisionNumber,
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
        UnitsPerPrint = unitsPerPrint;
        RevisionHash = revisionHash;
        RevisionNumber = revisionNumber;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    /// <summary>Detached adapter for existing production validation; never attach this object to EF.</summary>
    public static Part FromProductionSnapshot(OpenFarm.Domain.Production.ProductionPartSnapshot snapshot)
    {
        var part = new Part(snapshot.PartId, snapshot.OrganizationId, snapshot.Name, snapshot.FileFormat,
            snapshot.RequiresSlicing, snapshot.OriginalFileName, snapshot.StoragePath, snapshot.FileSizeBytes,
            snapshot.NozzleDiameterMm, snapshot.SlicerEngine, snapshot.SlicerVersion, snapshot.EstimateStatus,
            snapshot.EstimatedPrintTimeMinutes, snapshot.EstimatedFilamentWeightGrams, snapshot.PlateCount,
            snapshot.UnitsPerPrint, snapshot.RevisionHash, snapshot.RevisionNumber, DateTimeOffset.UtcNow)
        { MaterialMetadataComplete = snapshot.MaterialMetadataComplete };
        foreach (var requirement in snapshot.FilamentRequirements)
            part._filamentRequirements.Add(PartFilamentRequirement.Create(part.OrganizationId, part.Id, requirement));
        return part;
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

    public decimal? NozzleDiameterMm { get; private set; }

    public bool MaterialMetadataComplete { get; private set; } = true;

    public Guid? ActiveProcessingJobId { get; private set; }

    public string? EstimateUnavailableReason { get; private set; }

    public string? MetadataWarningsJson { get; private set; }
    public IReadOnlyList<string> MetadataWarnings => MetadataWarningsJson is null ? []
        : System.Text.Json.JsonSerializer.Deserialize<string[]>(MetadataWarningsJson) ?? [];
    public void SetMetadataWarnings(IReadOnlyList<string> warnings) => MetadataWarningsJson =
        System.Text.Json.JsonSerializer.Serialize(warnings);

    public string? EstimationDetailsJson { get; private set; }
    public EstimationDetails? Estimation => EstimationDetailsJson is null ? null
        : System.Text.Json.JsonSerializer.Deserialize<EstimationDetails>(EstimationDetailsJson);
    public void SetEstimation(EstimationDetails? details) => EstimationDetailsJson = details is null ? null
        : System.Text.Json.JsonSerializer.Serialize(details);

    public PrintRequirementsStatus PrintRequirementsStatus => NozzleDiameterMm is > 0
        && MaterialMetadataComplete && FilamentRequirements.Count > 0
        && (RequiresSlicing || FilamentRequirements.All(r => !string.IsNullOrWhiteSpace(r.MaterialType)))
            ? PrintRequirementsStatus.Complete : PrintRequirementsStatus.Incomplete;

    public IReadOnlyList<string> ProcessingWarnings => MetadataWarnings.Concat(PartProcessingWarnings.Create(
        NozzleDiameterMm, MaterialMetadataComplete, EstimateUnavailableReason)).Distinct().ToArray();

    public void SetProcessingMetadata(Guid jobId, bool materialMetadataComplete, string? estimateUnavailableReason)
    {
        ActiveProcessingJobId = jobId;
        MaterialMetadataComplete = materialMetadataComplete;
        EstimateUnavailableReason = estimateUnavailableReason;
    }

    public void EnsurePrintRequirementsComplete()
    {
        if (PrintRequirementsStatus != PrintRequirementsStatus.Complete)
            throw new PartPrintRequirementsIncompleteException(Id);
    }

    public void Reprocess(
        decimal? nozzleDiameterMm, SlicerEngine? engine, string? version, EstimateStatus estimateStatus,
        int? minutes, decimal? grams, int plateCount,
        IReadOnlyList<FilamentRequirementSpec> requirements, IReadOnlyList<ThumbnailSpec> thumbnails)
    {
        var uploadedAt = FileUploadedAtUtc;
        var revisionNumber = RevisionNumber;
        ReplaceFile(FileFormat, RequiresSlicing, OriginalFileName, StoragePath, FileSizeBytes,
            nozzleDiameterMm, engine, version, estimateStatus, minutes, grams, plateCount,
            RevisionHash, requirements, thumbnails);
        FileUploadedAtUtc = uploadedAt;
        RevisionNumber = revisionNumber;
    }

    public EstimateStatus EstimateStatus { get; private set; }

    public int? EstimatedPrintTimeMinutes { get; private set; }

    public decimal? EstimatedFilamentWeightGrams { get; private set; }

    public SlicerEngine? SlicerEngineOverride { get; private set; }

    /// <summary>The exact upstream version actually produced with (FR-005) — <c>null</c> for a non-slicing file or a legacy pre-feature row.</summary>
    public string? SlicerVersionUsed { get; private set; }

    public int PlateCount { get; private set; }

    /// <summary>FR-005a — default 1, auto-suggested from a sliced file's per-plate object count where parseable, always operator-overridable.</summary>
    public int UnitsPerPrint { get; private set; }

    /// <summary>FR-009c — SHA-256 hex of the current file's bytes; computed by the caller before Create/ReplaceFile (research.md R6).</summary>
    public string RevisionHash { get; private set; }

    /// <summary>FR-009c — starts at 1, incremented on every ReplaceFile.</summary>
    public int RevisionNumber { get; private set; }

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
        decimal? nozzleDiameterMm,
        SlicerEngine? slicerEngineOverride,
        string? slicerVersionUsed,
        EstimateStatus estimateStatus,
        int? estimatedPrintTimeMinutes,
        decimal? estimatedFilamentWeightGrams,
        int plateCount,
        int unitsPerPrint,
        string revisionHash,
        IReadOnlyList<FilamentRequirementSpec> filamentRequirements,
        IReadOnlyList<ThumbnailSpec> thumbnails,
        IReadOnlyList<Tag> tags)
    {
        ValidateSharedFields(name, originalFileName, storagePath, fileSizeBytes, nozzleDiameterMm, plateCount, unitsPerPrint, filamentRequirements);

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
            unitsPerPrint,
            revisionHash,
            1,
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

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Part name is required.", nameof(name));
        }

        Name = name.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// A direct, all-or-nothing replacement (never additive) — keeps <see cref="Name"/>/<see cref="Tags"/>
    /// untouched, replaces every file-derived field wholesale (data-model.md, FR-004).
    /// </summary>
    public void SetUnitsPerPrint(int unitsPerPrint)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(unitsPerPrint, 1);
        UnitsPerPrint = unitsPerPrint;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Replaces the upgrade migration's placeholder with the file's real SHA-256 without creating a new revision.</summary>
    public void MigrateLegacyFileRevision(string contentHash, string storagePath)
    {
        if (RevisionHash.Length == 64) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        if (contentHash.Length != 64 || contentHash.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("A file revision requires a SHA-256 hash.", nameof(contentHash));
        RevisionHash = contentHash;
        StoragePath = storagePath;
    }

    public void ReplaceFile(
        PartFileFormat fileFormat,
        bool requiresSlicing,
        string originalFileName,
        string storagePath,
        long fileSizeBytes,
        decimal? nozzleDiameterMm,
        SlicerEngine? slicerEngineOverride,
        string? slicerVersionUsed,
        EstimateStatus estimateStatus,
        int? estimatedPrintTimeMinutes,
        decimal? estimatedFilamentWeightGrams,
        int plateCount,
        string revisionHash,
        IReadOnlyList<FilamentRequirementSpec> filamentRequirements,
        IReadOnlyList<ThumbnailSpec> thumbnails)
    {
        ValidateSharedFields(Name, originalFileName, storagePath, fileSizeBytes, nozzleDiameterMm, plateCount, UnitsPerPrint, filamentRequirements);

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
        // research.md R6: content-addressed by revision hash — a new revision always increments,
        // even if (rare) it happens to hash-collide with a still-referenced earlier one.
        RevisionHash = revisionHash;
        RevisionNumber += 1;

        _filamentRequirements.Clear();
        foreach (var spec in filamentRequirements)
        {
            _filamentRequirements.Add(PartFilamentRequirement.Create(OrganizationId, Id, spec));
        }

        _thumbnails.Clear();
        foreach (var thumbnail in thumbnails)
        {
            _thumbnails.RemoveAll(t => t.PlateIndex == thumbnail.PlateIndex);
            _thumbnails.Add(PartThumbnail.Create(OrganizationId, Id, thumbnail.PlateIndex, thumbnail.Bytes));
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// The research.md §3b trailing continuation's success path — targets a still-<c>Resolving</c>
    /// part confirmed early (FR-009c/FR-009d). A no-op if the estimate has already resolved/failed by
    /// the time this runs (idempotency guard against a redundant continuation firing twice).
    /// </summary>
    public void ResolveEstimate(int? estimatedPrintTimeMinutes, decimal? estimatedFilamentWeightGrams, IReadOnlyList<ThumbnailSpec> thumbnails)
    {
        if (EstimateStatus != EstimateStatus.Resolving)
        {
            return;
        }

        EstimateStatus = PartEstimateAvailability.From(estimatedPrintTimeMinutes, estimatedFilamentWeightGrams);
        EstimateUnavailableReason = PartEstimateAvailability.MissingMetadataReason(estimatedPrintTimeMinutes, estimatedFilamentWeightGrams);
        EstimatedPrintTimeMinutes = estimatedPrintTimeMinutes;
        EstimatedFilamentWeightGrams = estimatedFilamentWeightGrams;

        foreach (var thumbnail in thumbnails)
        {
            _thumbnails.RemoveAll(t => t.PlateIndex == thumbnail.PlateIndex);
            _thumbnails.Add(PartThumbnail.Create(OrganizationId, Id, thumbnail.PlateIndex, thumbnail.Bytes));
        }

        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>The research.md §3b trailing continuation's failure path — permanent, no retry in this phase (FR-009d).</summary>
    public void MarkEstimateUnavailable(string? reason = null)
    {
        if (EstimateStatus != EstimateStatus.Resolving)
        {
            return;
        }

        EstimateStatus = PartEstimateAvailability.From(EstimatedPrintTimeMinutes, EstimatedFilamentWeightGrams);
        EstimateUnavailableReason = reason;
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
        decimal? nozzleDiameterMm,
        int plateCount,
        int unitsPerPrint,
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

        if (unitsPerPrint < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(unitsPerPrint), "Units per print must be at least 1.");
        }

    }
}
