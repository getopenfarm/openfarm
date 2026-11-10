namespace OpenFarm.Infrastructure.Identity;

/// <summary>A dynamically registered OAuth client. Registering authorizes nothing; a user's consent does.</summary>
public sealed class OAuthClient
{
    public const string IdPrefix = "ofc_";

    /// <summary><c>ofc_</c> + 32 lowercase hex.</summary>
    public string Id { get; set; } = "";

    /// <summary>Supplied by the client: an untrusted label.</summary>
    public string Name { get; set; } = "";
    public string[] RedirectUris { get; set; } = [];

    /// <summary><c>none</c>, <c>client_secret_basic</c> or <c>client_secret_post</c>.</summary>
    public string AuthMethod { get; set; } = "";

    /// <summary>SHA-256 of the secret, upper hex; null for public (<c>none</c>) clients.</summary>
    public string? SecretHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
