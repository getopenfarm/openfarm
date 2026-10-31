using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Security;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Auth;

public class SessionCookieTests
{
    [Theory]
    [InlineData(false, "lax")]
    [InlineData(true, "none")]
    public void Clear_UsesTheSameCookieScopeAndSecurityAsCreation(bool crossSite, string sameSite)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:CrossSiteCookies"] = crossSite.ToString(),
        }).Build();
        using var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        SessionCookies.Set(context, new LoginResult("token", DateTimeOffset.UtcNow.AddMinutes(15), Guid.NewGuid(), "credential", DateTimeOffset.UtcNow.AddDays(14)));
        var created = context.Response.Headers.SetCookie.ToString().ToLowerInvariant();
        context.Response.Headers.Clear();
        SessionCookies.Clear(context);
        var cleared = context.Response.Headers.SetCookie.ToString().ToLowerInvariant();
        foreach (var required in new[] { "path=/api/v1/auth", "secure", "httponly", $"samesite={sameSite}" })
        {
            created.Should().Contain(required);
            cleared.Should().Contain(required);
        }
        cleared.Should().Contain("expires=");
    }
}
