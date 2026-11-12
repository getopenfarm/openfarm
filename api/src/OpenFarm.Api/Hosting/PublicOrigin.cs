using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Hosting;

/// <summary>The canonical public address from the current request configuration, never request headers.</summary>
public sealed class PublicOrigin
{
    public string? Value { get; }
    public bool IsHttps => Value?.StartsWith("https://", StringComparison.Ordinal) == true;

    public PublicOrigin(IConfiguration configuration) : this(configuration["ORIGIN"]) { }

    public PublicOrigin(string? value) => Value = ServerConfigurationOverrides.NormalizeOrigin(value);
}
