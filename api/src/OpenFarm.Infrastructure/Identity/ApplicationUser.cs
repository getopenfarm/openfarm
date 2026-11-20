using Microsoft.AspNetCore.Identity;

namespace OpenFarm.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? ProvisioningProvider { get; set; }
    public string? ProvisioningExternalId { get; set; }

    /// <summary>
    /// The groups the OIDC group sync assigned at this user's last sign-in. Only the sync writes it: it is how a
    /// removed or retargeted mapping can still revoke what it granted, without touching groups assigned by hand.
    /// </summary>
    public Guid[] ProvisionedGroupIds { get; set; } = [];
}
