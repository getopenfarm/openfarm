using OpenFarm.Domain.Parts;

namespace OpenFarm.Domain.Organizations;

public sealed class Organization
{
    public static readonly Guid WellKnownId = new("00000000-0000-0000-0000-000000000001");

    private Organization(Guid id, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        DefaultSlicerEngine = SlicerEngine.OrcaSlicer;
        DefaultSlicerVersion = null;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; }

    /// <summary>
    /// The engine half of the optional default <see cref="SlicerRef"/> pair (FR-007, research.md
    /// §5/§10). Meaningful only when <see cref="DefaultSlicerVersion"/> is also set — a
    /// <c>null</c> version means no explicit default has ever been set, and the effective default
    /// is computed at read time by <c>ResolveSlicerSelection</c> instead.
    /// </summary>
    public SlicerEngine DefaultSlicerEngine { get; private set; }

    /// <summary>The version half of the optional default pair; <c>null</c> ⇒ no explicit default set (data-model.md "Organization").</summary>
    public string? DefaultSlicerVersion { get; private set; }

    public Guid? EstimationMachineProfileId { get; private set; }
    public Guid? EstimationProcessProfileId { get; private set; }
    public Guid? EstimationFilamentProfileId { get; private set; }
    public OpenFarm.Domain.Printers.BuildPlateType? EstimationBuildPlateType { get; private set; }

    public void SetEstimationProfiles(Guid? machineId, Guid? processId, Guid? filamentId,
        OpenFarm.Domain.Printers.BuildPlateType? buildPlate)
    {
        if ((machineId is null || processId is null || filamentId is null || buildPlate is null)
            && (machineId is not null || processId is not null || filamentId is not null || buildPlate is not null))
            throw new ArgumentException("Estimation profiles must be configured together or cleared together.");
        EstimationMachineProfileId = machineId;
        EstimationProcessProfileId = processId;
        EstimationFilamentProfileId = filamentId;
        EstimationBuildPlateType = buildPlate;
    }

    public DateTimeOffset CreatedAtUtc { get; }

    public static Organization Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Organization name is required.", nameof(name));
        }

        return new Organization(WellKnownId, name, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Sets the explicit org default — the caller (<c>UpdateSlicingDefaultsCommandHandler</c>) MUST
    /// have already validated <paramref name="slicer"/> against the bundle via
    /// <c>ResolveSlicerSelection.EnsureAvailable</c> (FR-007); this method itself performs no
    /// availability check.
    /// </summary>
    public void SetDefaultSlicer(SlicerRef slicer)
    {
        ArgumentNullException.ThrowIfNull(slicer);
        DefaultSlicerEngine = slicer.Engine;
        DefaultSlicerVersion = slicer.Version;
    }

    /// <summary>The explicit default as a <see cref="SlicerRef"/>, or <c>null</c> when none has ever been set.</summary>
    public SlicerRef? DefaultSlicer => DefaultSlicerVersion is { } version ? new SlicerRef(DefaultSlicerEngine, version) : null;
}
