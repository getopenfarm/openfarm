using System.IdentityModel.Tokens.Jwt;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenFarm.Api.Security;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Common;

namespace OpenFarm.Api.Mcp.Tools;

/// <param name="Credential"><c>Token</c> for a personal access token.</param>
/// <param name="CredentialLabel">The token's name.</param>
/// <param name="Permissions">What this connection may use: the account's permissions narrowed to the credential.</param>
public sealed record McpWhoAmI(string Username, string? DisplayName, string Credential, string CredentialLabel, string[] Permissions);

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
                return new McpWhoAmI(me.Username, me.DisplayName, "Token",
                    http.User.FindFirst(PersonalAccessTokenHandler.NameClaim)?.Value ?? "",
                    [.. permissions.Order(StringComparer.Ordinal)]);
            }),
    ];
}
