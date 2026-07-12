using Finbuckle.MultiTenant.Abstractions;

namespace OpenFarm.Infrastructure.Multitenancy;

public sealed class AppTenantInfo : ITenantInfo
{
    public string Id { get; set; } = string.Empty;

    public string Identifier { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? ConnectionString { get; set; }
}
