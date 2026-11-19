using Microsoft.Extensions.Logging;

namespace OpenFarm.Infrastructure.Auth;

/// <summary>
/// Every refused OIDC sign-in redirects to the same generic login message, so this names the cause for the administrator.
/// Reasons are fixed codes: never pass claim values. Fixes: docs/content/administration/account-provisioning.md.
/// </summary>
public static partial class OidcSignInLog
{
    [LoggerMessage(EventId = 4105, EventName = "OidcSignInRejected", Level = LogLevel.Warning, Message = "OIDC sign-in rejected: {Reason}.")]
    public static partial void Rejected(ILogger logger, string reason);

    // Information, not Warning: a user with no mapped entitlements legitimately has an empty claim. Never log claim values.
    [LoggerMessage(EventId = 4106, EventName = "OidcGroupsClaimEmpty", Level = LogLevel.Information,
        Message = "The OIDC ID token has no values in the groups claim '{Claim}'. The user gets the default group. If this is unexpected, check Groups claim in Settings → Authentication and the provider's scope mapping.")]
    public static partial void GroupsClaimEmpty(ILogger logger, string claim);
}
