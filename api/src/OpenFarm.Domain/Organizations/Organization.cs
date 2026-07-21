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

    /// <summary>
    /// The org's default <c>MachineProfile</c> for headless slicing — <c>null</c> ⇒ not yet
    /// configured, in which case slicing against OrcaSlicer/Bambu Studio fails with a clear
    /// "unconfigured" error rather than falling back to a guess (no printer is assigned yet at
    /// Part-processing time, so there is no per-printer profile to derive this from).
    /// </summary>
    public Guid? DefaultMachineProfileId { get; private set; }

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

    public void SetDefaultMachineProfile(Guid machineProfileId) => DefaultMachineProfileId = machineProfileId;

    public void ClearDefaultMachineProfile() => DefaultMachineProfileId = null;
}
