namespace OpenFarm.Infrastructure.Identity;

/// <summary>External group resources are separate from local roles. Only configured mappings grant access.</summary>
public sealed class ProvisioningGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string NormalizedDisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
}

public sealed class ProvisioningGroupMember
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
}
