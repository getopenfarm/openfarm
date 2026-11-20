namespace OpenFarm.Infrastructure.Auth;

public enum OidcLinkFailure
{
    Expired,
    ConfigurationChanged,
    SessionChanged,
    RequestReplaced,
    AlreadyLinked,
    IdentityConflict,
    Failed,
}

public sealed class OidcLinkException(OidcLinkFailure failure, string message) : ArgumentException(message)
{
    public OidcLinkFailure Failure { get; } = failure;
}
