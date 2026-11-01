using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OpenFarm.Infrastructure.Auth;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.Security;

public sealed class PermissionRequirement : IAuthorizationRequirement;

public sealed class PermissionAuthorizationHandler(PermissionService permissions, OpenFarmDbContext db) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.Resource is not HttpContext http || context.User.Identity?.IsAuthenticated != true
            || !Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)) return;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsEnabled, http.RequestAborted);
        if (user is null) return;
        var endpoint = http.GetEndpoint();
        var metadata = endpoint?.Metadata.GetMetadata<PermissionAttribute>();
        // FastEndpoints exposes endpoint attributes via its endpoint definition as well.
        metadata ??= endpoint?.Metadata.GetMetadata<FastEndpoints.EndpointDefinition>()?.EndpointAttributes?
            .OfType<PermissionAttribute>().SingleOrDefault();
        var permission = metadata?.Permission;
        if (permission is null) return;
        if (permission == "auth:whoami" || permission == "auth:self")
        {
            context.Succeed(requirement);
            return;
        }
        var access = await permissions.ResolveAsync(userId, http.RequestAborted);
        var required = new List<string> { permission };
        required.AddRange(metadata!.AdditionalPermissions);
        if (permission.StartsWith("library:", StringComparison.Ordinal))
        {
            var library = http.Request.RouteValues["library"]?.ToString();
            if (library == "parts") required.Add("parts:list");
            else if (library == "assemblies") required.Add("assemblies:view");
            else return;
        }
        if (required.All(p => access.Permissions.Contains(p, StringComparer.Ordinal))) context.Succeed(requirement);
    }
}
