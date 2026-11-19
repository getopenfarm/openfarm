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
}
