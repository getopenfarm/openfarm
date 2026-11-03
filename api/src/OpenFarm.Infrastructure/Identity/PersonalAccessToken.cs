namespace OpenFarm.Infrastructure.Identity;

public sealed class PersonalAccessToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string CredentialHash { get; set; } = "";
    public string LastFour { get; set; } = "";
    public string[] Permissions { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset? LastUsedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
