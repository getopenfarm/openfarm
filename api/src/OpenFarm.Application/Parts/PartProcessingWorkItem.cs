namespace OpenFarm.Application.Parts;

/// <summary>Queue payload consumed by <c>PartProcessingBackgroundService</c> — the job row itself carries every other detail.</summary>
public sealed record PartProcessingWorkItem(Guid OrganizationId, Guid JobId);
