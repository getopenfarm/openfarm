namespace OpenFarm.Application.Profiles;

/// <summary>The printer and material a resolved set of presets is about to be used with.</summary>
public sealed record ProfileUseContext(string Brand, string ModelCode, decimal NozzleMm, string MaterialType);
