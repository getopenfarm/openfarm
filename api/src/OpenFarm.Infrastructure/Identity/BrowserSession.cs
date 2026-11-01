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
}

public sealed class SessionCredential
{
    public string Hash { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    // Encrypted successor permits idempotent refresh retries during a short grace period.
    public string? ProtectedSuccessor { get; set; }
}
