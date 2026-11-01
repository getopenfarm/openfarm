using System.Reflection;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Security;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Auth;

public class PermissionCoverageTests
{
    [Fact]
    public void EveryProtectedEndpoint_DeclaresAKnownPermission()
    {
        string[] anonymous = ["LoginEndpoint", "RefreshEndpoint", "CsrfEndpoint", "LogoutEndpoint", "PingEndpoint"];
        var endpoints = typeof(LoginEndpoint).Assembly.GetTypes().Where(t => t.Name.EndsWith("Endpoint", StringComparison.Ordinal)
            && t.BaseType?.Namespace == "FastEndpoints" && !t.IsAbstract).ToArray();
        endpoints.Length.Should().BeGreaterThan(100);
        foreach (var endpoint in endpoints.Where(t => !anonymous.Contains(t.Name, StringComparer.Ordinal)))
        {
            var permission = endpoint.GetCustomAttribute<PermissionAttribute>();
            permission.Should().NotBeNull($"{endpoint.Name} must declare access explicitly");
            foreach (var key in new[] { permission!.Permission }.Concat(permission.AdditionalPermissions))
                (key is "auth:whoami" or "auth:self" || PermissionCatalog.IsKnown(key)).Should().BeTrue($"{endpoint.Name}: {key}");
        }
    }
}
