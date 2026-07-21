namespace OpenFarm.Application.Parts;

public sealed record GetSlicingProfileDefaultsQuery;

public sealed record DefaultProcessProfileInfo(Guid Id, string Name);

public sealed record DefaultMachineProfileInfo(Guid Id, string Name, DefaultProcessProfileInfo? DefaultProcessProfile);

public sealed record FilamentDefaultInfo(string MaterialType, Guid FilamentProfileId, string FilamentProfileName);

public sealed record SlicingProfileDefaults(
    DefaultMachineProfileInfo? DefaultMachineProfile,
    IReadOnlyList<FilamentDefaultInfo> FilamentDefaults);
