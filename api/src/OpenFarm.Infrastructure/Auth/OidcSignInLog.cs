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

    // Information, not Warning: a user with no mapped entitlements legitimately has an empty claim, and some providers leave an
    // empty claim out. Claim NAMES are logged so an administrator can see what the provider sent; never log claim values. The
    // logger formats a list argument as comma-separated values, so callers pass the lists as they are (no joining at the call site).
    [LoggerMessage(EventId = 4106, EventName = "OidcGroupsClaimEmpty", Level = LogLevel.Information,
        Message = "The OIDC ID token has the groups claim '{Claim}', but it lists no values. The user gets the default group. "
            + "If this is unexpected: the provider lists no group or entitlement for this user; check the user's group memberships "
            + "or entitlement bindings at the provider.")]
    public static partial void GroupsClaimEmpty(ILogger logger, string claim);

    [LoggerMessage(EventId = 4107, EventName = "OidcGroupsClaimMissing", Level = LogLevel.Information,
        Message = "The OIDC ID token has no groups claim '{Claim}'. Claims present: {Present}. Scopes requested: {Scopes}. "
            + "The user gets the default group. If this is unexpected: check Groups claim in Settings → Authentication; the provider's scope mapping "
            + "must be attached to the provider and its scope name listed in Additional scopes; the provider must include claims in the ID token.")]
    public static partial void GroupsClaimMissing(ILogger logger, string claim, IEnumerable<string>? present, IEnumerable<string> scopes);
}
