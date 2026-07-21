namespace OpenFarm.Application.Parts;

public sealed record UpdateOrganizationFilamentDefaultCommand(string MaterialType, Guid FilamentProfileId);

public sealed record OrganizationFilamentDefaultResult(string MaterialType, Guid FilamentProfileId);
