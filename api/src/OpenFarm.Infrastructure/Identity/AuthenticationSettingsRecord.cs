namespace OpenFarm.Infrastructure.Identity;

/// <summary>Credentials and provider settings are protected with the installation's persistent data-protection keys.</summary>
public sealed class AuthenticationSettingsRecord
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");
    public Guid Id { get; set; } = SingletonId;
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string ProtectedConfiguration { get; set; } = string.Empty;
}

/// <summary>Short-lived logout receipts prevent replay and completion of an in-flight sign-in after logout.</summary>
public sealed class OidcLogoutReceipt
{
    public string Id { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string? SessionId { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
