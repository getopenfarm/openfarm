using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Security;

public sealed class ScimAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, ProvisioningSettings settings) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ScimProvisioning";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!settings.ScimEnabled || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());
        var token = header[7..];
        if (token.Length > 512 || !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)), SHA256.HashData(Encoding.UTF8.GetBytes(settings.ScimToken!))))
            return Task.FromResult(AuthenticateResult.Fail("Invalid provisioning credential."));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("scope", "scim")], SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer realm=\"OpenFarm SCIM\"";
        return ScimEndpoints.ErrorAsync(Context, 401, "A valid SCIM provisioning credential is required.");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) => ScimEndpoints.ErrorAsync(Context, 403, "SCIM access denied.");
}
