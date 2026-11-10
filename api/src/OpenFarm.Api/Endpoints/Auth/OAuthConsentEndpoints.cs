using FastEndpoints;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;
using Microsoft.AspNetCore.WebUtilities;
using OpenFarm.Api.Security;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Auth.OAuth;

namespace OpenFarm.Api.Endpoints.Auth;

public sealed record ConsentRequest(string? Request);

/// <param name="Request">The <c>request</c> value from the consent page URL.</param>
/// <param name="Approve">False for Cancel: the app gets <c>access_denied</c>.</param>
/// <param name="Scopes">The ticked scopes; each must be allowed for the user now.</param>
public sealed record ConsentDecisionRequest(string Request, bool Approve, string[] Scopes);

/// <param name="RedirectUri">Where to send the browser next. Use exactly this value.</param>
public sealed record ConsentDecisionResponse(string RedirectUri);

/// <summary>
/// The consent page's data. Browser sessions only (<c>/api/v1/auth</c> refuses tokens; app tokens fail the browser
/// audience). Errors are ProblemDetails with <c>code</c>: <c>invalid_request</c> (expired, used or unknown
/// request), <c>invalid_scope</c> (nothing allowed selected).
/// </summary>
[Permission("auth:self")]
public sealed class GetConsentEndpoint(OAuthSettings settings, OAuthService oauth) : Endpoint<ConsentRequest, ConsentResponse>
{
    public override void Configure() { Get("/api/v1/auth/oauth/consent"); Policies("Permission"); }

    public override async Task HandleAsync(ConsentRequest req, CancellationToken ct)
    {
        if (!settings.Enabled) { await Send.NotFoundAsync(ct); return; }
        try { Response = await oauth.DescribeAsync(req.Request, SessionCookies.UserId(HttpContext), ct); }
        catch (OAuthException e) { await ConsentProblem.SendAsync(HttpContext, e, ct); }
    }
}

[Permission("auth:self")]
public sealed class DecideConsentEndpoint(OAuthSettings settings, OAuthService oauth) : Endpoint<ConsentDecisionRequest, ConsentDecisionResponse>
{
    public override void Configure() { Post("/api/v1/auth/oauth/consent"); Policies("Permission"); Throttle(20, 60); }

    public override async Task HandleAsync(ConsentDecisionRequest req, CancellationToken ct)
    {
        if (!settings.Enabled) { await Send.NotFoundAsync(ct); return; }
        try
        {
            var outcome = await oauth.DecideAsync(req.Request, SessionCookies.UserId(HttpContext), req.Approve, req.Scopes, ct);
            // Keeps a query the client registered on its redirect URI.
            Response = new(QueryHelpers.AddQueryString(outcome.RedirectUri, outcome.Query));
        }
        catch (OAuthException e) { await ConsentProblem.SendAsync(HttpContext, e, ct); }
    }
}

internal static class ConsentProblem
{
    public static Task SendAsync(HttpContext http, OAuthException e, CancellationToken ct)
    {
        var problem = e.Code == "invalid_scope"
            ? new ProblemDetails { Title = "Select at least one available permission.", Detail = "Select at least one available permission." }
            : new ProblemDetails { Title = "Connection request expired", Detail = "Start again from your AI assistant." };
        problem.Status = StatusCodes.Status400BadRequest;
        problem.Extensions["code"] = e.Code == "invalid_scope" ? "invalid_scope" : "invalid_request";
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return http.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
    }
}
