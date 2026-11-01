using Microsoft.AspNetCore.Antiforgery;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.Security;

public static class SessionCookies
{
    public const string RefreshName = "openfarm.refresh";

    public static void Set(HttpContext context, LoginResult result)
    {
        context.Response.Headers.CacheControl = "no-store";
        var options = Options(context);
        options.Expires = result.SessionExpiresAtUtc;
        context.Response.Cookies.Append(RefreshName, result.RefreshToken!, options);
    }

    public static void Clear(HttpContext context) => context.Response.Cookies.Delete(RefreshName, Options(context));

    private static CookieOptions Options(HttpContext context)
    {
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        var crossSite = configuration.GetValue("Auth:CrossSiteCookies", false);
        return new CookieOptions
        {
            Path = "/api/v1/auth",
            HttpOnly = true,
            Secure = context.Request.IsHttps || crossSite || configuration.GetValue("Auth:SecureCookies", false),
            SameSite = crossSite ? SameSiteMode.None : SameSiteMode.Lax,
        };
    }

    public static async Task<bool> ValidateAsync(HttpContext context)
    {
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { detail = "Invalid antiforgery token." }, context.RequestAborted);
            return false;
        }
    }

    public static Guid UserId(HttpContext context) => Guid.Parse(context.User.FindFirst("sub")!.Value);
}
