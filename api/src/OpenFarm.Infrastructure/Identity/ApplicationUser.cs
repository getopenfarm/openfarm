using Microsoft.AspNetCore.Identity;

namespace OpenFarm.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? ProvisioningProvider { get; set; }
    public string? ProvisioningExternalId { get; set; }
    public string? ProvisioningGivenName { get; set; }
    public string? ProvisioningFamilyName { get; set; }
    public DateTimeOffset? ProvisioningModifiedAtUtc { get; set; }
    public Guid ProvisioningRevision { get; set; }
    public bool IsScimDeleted { get; set; }
}
