using System.IdentityModel.Tokens.Jwt;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Security;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Common;

namespace OpenFarm.Api.Mcp.Tools;

/// <param name="Credential"><c>Token</c> for a personal access token, <c>App</c> for an app signed in with OAuth.</param>
/// <param name="CredentialLabel">The token's name, or the app's name as it registered itself.</param>
/// <param name="Permissions">What this connection may use: the account's permissions narrowed to the credential.</param>
/// <param name="Scopes">The OAuth scopes granted to an app; empty for a token.</param>
public sealed record McpWhoAmI(string Username, string? DisplayName, string Credential, string CredentialLabel, string[] Permissions, string[] Scopes);

public static class AccountTools
{
    public static readonly McpToolSpec[] All =
    [
        McpTools.Read("whoami", "Who am I",
            "The OpenFarm account and credential this connection uses, and the permissions it may use.",
            async (RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
            {
                var http = request.Service<IHttpContextAccessor>().HttpContext!;
                var userId = Guid.Parse(http.User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
                var me = McpTools.Found(await request.Service<IQueryHandler<WhoAmIQuery, WhoAmIResult?>>()
                    .HandleAsync(new WhoAmIQuery(userId), ct), "Account");
                var permissions = await CredentialAccess.EffectiveAsync(http);
                var via = CredentialClaims.Channel(http.User)!;
                return new McpWhoAmI(me.Username, me.DisplayName, via.Kind, via.Label, [.. permissions.Order(StringComparer.Ordinal)],
                    [.. http.User.FindAll(CredentialClaims.Scope).Select(c => c.Value)]);
            }),
    ];
}
