namespace OpenFarm.Infrastructure.Identity;

public sealed class BrowserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string Device { get; set; } = string.Empty;

    /// <summary>Set for a connected app (an OAuth grant); null for a browser sign-in. Every query states which kind it wants.</summary>
    public string? ClientId { get; set; }

    /// <summary>The OAuth scopes an app was granted; null exactly when <see cref="ClientId"/> is null.</summary>
    public string[]? Scopes { get; set; }

    /// <summary>The redirect URI the user consented to (an app only): what "Returns to" shows.</summary>
    public string? RedirectUri { get; set; }

    public string? OAuthConfigurationIdentity { get; set; }
}

public sealed class SessionCredential
{
    public string Hash { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    // Encrypted successor permits idempotent refresh retries during a short grace period.
    public string? ProtectedSuccessor { get; set; }
}
