using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OpenFarm.Api.Hosting;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Security;

public static class OidcAuthentication
{
    public const string Scheme = "ExternalOidc";
    public const string CallbackPath = "/api/v1/auth/sso/callback";
    public const string TestCookie = "openfarm.oidc.test";
    public const string LinkCookie = "openfarm.oidc.link";
    private const string LinkStarted = "openfarm.link.started";
    private static readonly Action<ILogger, string?, Exception?> ChallengeFailed = LoggerMessage.Define<string?>(
        LogLevel.Warning, new EventId(4100, "OidcChallengeFailed"),
        "Could not start OIDC sign-in for issuer {Issuer}. Check provider discovery and connectivity.");

    public static async Task StartChallengeAsync(HttpContext http, AuthenticationProperties properties)
    {
        try { await http.ChallengeAsync(Scheme, properties); }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException)
        {
            var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OpenFarm.OidcAuthentication");
            ChallengeFailed(logger, http.RequestServices.GetRequiredService<OidcSettings>().Authority, e);
            // Keep protocol details in server logs; expose only a fixed, actionable failure category.
            http.Response.Redirect(properties.Items.ContainsKey("test")
                ? "/settings/authentication?test=failed&ssoReason=provider_configuration"
                : properties.Items.ContainsKey("link")
                    ? "/profile?tab=security&sso=failed&ssoReason=provider_configuration"
                    : "/login?ssoError=provider_configuration");
        }
    }

    public static void AddExternalOidc(this AuthenticationBuilder builder)
    {
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<OidcMetadataCache>();
        builder.Services.AddHttpClient("OidcMetadata");
        // The handler requires a sign-in scheme. OnTicketReceived always handles the response;
        // no external authentication cookie is issued or accepted by API authorization.
        builder.AddCookie("OidcExternal", options => options.ExpireTimeSpan = TimeSpan.FromMinutes(5))
            .AddScheme<OpenIdConnectOptions, DynamicOidcHandler>(Scheme, options =>
            {
                options.SignInScheme = "OidcExternal";
                // Valid baseline for the disabled scheme. DynamicOidcHandler creates request-local options.
                options.Authority = "https://oidc.invalid/";
                options.ClientId = "disabled";
                options.CallbackPath = CallbackPath;
                options.RemoteSignOutPath = PathString.Empty;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                options.MapInboundClaims = false;
                // Keep the validated issuer available for the explicit authority binding below.
                options.ClaimActions.Clear();
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = false;
                options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.UseIfAvailable;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(5);
                // A GET callback permits Lax cookies, including the existing refresh cookie used for linking.
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                // Resolve the public URL per request, including deployments with TLS termination.
                // Wrap the framework builders to preserve nonce expiration and cookie paths.
                options.CorrelationCookie = new PublicOriginCookieBuilder(options.CorrelationCookie);
                options.NonceCookie = new PublicOriginCookieBuilder(options.NonceCookie);
                options.Events = new OpenIdConnectEvents
                {
                    OnRedirectToIdentityProvider = context =>
                    {
                        var settings = context.HttpContext.RequestServices.GetRequiredService<OidcSettings>();
                        var origin = context.HttpContext.RequestServices.GetRequiredService<PublicOrigin>().Value
                            ?? throw new InvalidOperationException("OIDC needs a configured public URL.");
                        settings.ValidatePublicUrl(origin);
                        context.ProtocolMessage.RedirectUri = origin + CallbackPath;
                        context.Properties.Items["openfarm.oidc.origin"] = origin;
                        context.Properties.Items["openfarm.oidc.configuration"] = settings.SessionIdentity;
                        context.Properties.Items["openfarm.oidc.revision"] = context.HttpContext.RequestServices
                            .GetRequiredService<AuthenticationConfiguration>().Revision.ToString();
                        context.Properties.Items["openfarm.oidc.started"] = context.HttpContext.RequestServices
                            .GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
                        if (context.Properties.Items.ContainsKey("link"))
                        {
                            context.Properties.Items[LinkStarted] = context.HttpContext.RequestServices
                                .GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                            context.ProtocolMessage.Prompt = "login";
                            context.ProtocolMessage.MaxAge = "0";
                        }
                        return Task.CompletedTask;
                    },
                    OnTicketReceived = CompleteAsync,
                    OnRemoteFailure = context =>
                    {
                        context.HandleResponse();
                        context.Response.Redirect(context.Properties?.Items.ContainsKey("test") == true ? "/settings/authentication?test=failed" : "/login?ssoError=failed");
                        return Task.CompletedTask;
                    },
                };
            });
    }

    // Runs after code, signature, audience, nonce and correlation validation have completed.
    private static async Task CompleteAsync(TicketReceivedContext context)
    {
        context.HandleResponse();
        var http = context.HttpContext;
        var settings = http.RequestServices.GetRequiredService<OidcSettings>();
        var subjects = context.Principal?.FindAll("sub").ToArray() ?? [];
        var issuers = context.Principal?.FindAll("iss").ToArray() ?? [];
        if (!settings.Enabled || subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0].Value) || subjects[0].Value.Length > 255
            || issuers.Length != 1 || issuers[0].Value != settings.Authority)
        {
            http.Response.Redirect(context.Properties?.Items.ContainsKey("test") == true ? "/settings/authentication?test=failed" : "/login?ssoError=failed");
            return;
        }
        if (context.Properties?.Items.TryGetValue("openfarm.oidc.configuration", out var challengedIdentity) != true
            || challengedIdentity != settings.SessionIdentity
            || context.Properties.Items.TryGetValue("openfarm.oidc.revision", out var revision) != true
            || revision != http.RequestServices.GetRequiredService<AuthenticationConfiguration>().Revision.ToString())
        {
            http.Response.Redirect(context.Properties?.Items.ContainsKey("test") == true ? "/settings/authentication?test=failed" : "/login?ssoError=failed");
            return;
        }
        var accounts = http.RequestServices.GetRequiredService<OidcAccountService>();
        var ct = http.RequestAborted;
        if (context.Properties?.Items.TryGetValue("test", out var testTicket) == true)
        {
            http.Response.Cookies.Delete(TestCookie, LinkCookieOptions(http));
            var passed = !string.IsNullOrEmpty(testTicket) && http.Request.Cookies[TestCookie] == testTicket
                && await accounts.CompleteTestAsync(testTicket, http.Request.Cookies[SessionCookies.RefreshName] ?? "", subjects[0].Value, context.Properties.Items.TryGetValue("openfarm.oidc.origin", out var challengedOrigin) ? challengedOrigin : null, ct);
            http.Response.Redirect("/settings/authentication?test=" + (passed ? "success" : "failed"));
            return;
        }
        if (context.Properties?.Items.TryGetValue("link", out var ticket) == true)
        {
            var cookie = http.Request.Cookies[LinkCookie];
            http.Response.Cookies.Delete(LinkCookie, LinkCookieOptions(http));
            try
            {
                if (string.IsNullOrEmpty(ticket) || cookie != ticket) throw new ArgumentException("Missing linking proof.");
                var times = context.Principal!.FindAll("auth_time").ToArray();
                var now = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds();
                // max_age is a request, not proof. Check the signed authentication time against
                // the challenge time carried in protected state, allowing one minute of clock skew.
                if (!context.Properties.Items.TryGetValue(LinkStarted, out var start)
                    || !long.TryParse(start, NumberStyles.None, CultureInfo.InvariantCulture, out var started)
                    || times.Length != 1
                    || !long.TryParse(times[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var authenticated)
                    || authenticated < started - 60 || authenticated > now + 60)
                    throw new ArgumentException("Fresh authentication is required to link an identity.");
                await accounts.CompleteLinkAsync(ticket, http.Request.Cookies[SessionCookies.RefreshName] ?? "", subjects[0].Value, ct);
                http.Response.Redirect("/profile?tab=security&sso=linked");
            }
            catch (ArgumentException) { http.Response.Redirect("/profile?tab=security&sso=failed"); }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            { http.Response.Redirect("/profile?tab=security&sso=failed"); }
            return;
        }
        string? Claim(string name) => context.Principal!.FindAll(name).Select(c => c.Value).SingleOrDefault();
        OpenFarm.Application.Auth.LoginResult? result;
        try
        {
            var profile = new ExternalProfile(Claim("preferred_username"), Claim("email"), Claim("name"),
                string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase));
            var providerSessionId = Claim("sid");
            if (providerSessionId is { Length: > 255 } || providerSessionId is not null && string.IsNullOrWhiteSpace(providerSessionId))
                throw new ArgumentException("Invalid provider session identifier.");
            if (context.Properties?.Items.TryGetValue("openfarm.oidc.started", out var start) != true
                || !long.TryParse(start, NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds))
                throw new ArgumentException("Missing authorization start time.");
            result = await accounts.SignInAsync(subjects[0].Value, http.Request.Headers.UserAgent.ToString(), ct, profile,
                providerSessionId, DateTimeOffset.FromUnixTimeMilliseconds(milliseconds));
        }
        catch (ArgumentException) { result = null; }
        catch (InvalidOperationException) { result = null; }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { result = null; }
        if (result is null)
        {
            http.Response.Redirect("/login?ssoError=unavailable");
            return;
        }
        SessionCookies.Set(http, result);
        var target = context.Properties?.Items.TryGetValue("returnTo", out var value) == true ? value : null;
        http.Response.Redirect("/auth/complete?returnTo=" + Uri.EscapeDataString(LocalReturnTo(target)));
    }

    private sealed class PublicOriginCookieBuilder : CookieBuilder
    {
        private readonly CookieBuilder _original;

        public PublicOriginCookieBuilder(CookieBuilder original)
        {
            _original = original;
            Name = original.Name;
        }

        public override CookieOptions Build(HttpContext context, DateTimeOffset expiresFrom)
        {
            var cookie = _original.Build(context, expiresFrom);
            cookie.Secure = context.Request.IsHttps || context.RequestServices.GetRequiredService<PublicOrigin>().IsHttps;
            return cookie;
        }
    }

    public static CookieOptions LinkCookieOptions(HttpContext http) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Path = "/api/v1/auth/sso",
        Secure = http.Request.IsHttps || http.RequestServices.GetRequiredService<PublicOrigin>().IsHttps,
        MaxAge = TimeSpan.FromMinutes(5),
    };

    public static string LocalReturnTo(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 2048
        && value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)
        && !value.Any(c => char.IsControl(c) || c == '\\')
        && !value.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith("/auth/complete", StringComparison.OrdinalIgnoreCase)
            ? value : "/";
}
