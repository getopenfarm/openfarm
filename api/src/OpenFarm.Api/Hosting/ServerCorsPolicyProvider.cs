using Microsoft.AspNetCore.Cors.Infrastructure;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Hosting;

/// <summary>Builds CORS from the same request snapshot used for cookies, auth origins and OAuth.</summary>
public sealed class ServerCorsPolicyProvider(IConfiguration configuration) : ICorsPolicyProvider
{
    public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        var origin = context.RequestServices.GetRequiredService<ServerConfiguration>().Current?.PublicUrl;
        var additional = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var policy = new CorsPolicyBuilder().WithOrigins(origin is null ? additional : [.. additional, origin])
            .AllowCredentials().AllowAnyHeader().AllowAnyMethod()
            .WithExposedHeaders("Content-Disposition", "ETag", OpenFarm.Api.Endpoints.Printers.GetPrinterCameraFrameEndpoint.ReceivedAtHeader)
            .Build();
        return Task.FromResult<CorsPolicy?>(policy);
    }
}
