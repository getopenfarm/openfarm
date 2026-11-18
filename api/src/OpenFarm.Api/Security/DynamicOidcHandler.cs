using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OpenFarm.Infrastructure.Auth;

namespace OpenFarm.Api.Security;

/// <summary>Discovery/key rollover state is shared, while client credentials and protocol options belong to one request.</summary>
public sealed class OidcMetadataCache(IMemoryCache cache, IHttpClientFactory clients)
{
    public IConfigurationManager<OpenIdConnectConfiguration> Get(string authority) => cache.GetOrCreate(
        "OpenFarm.OidcMetadata:" + authority, entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromHours(1);
            return new ConfigurationManager<OpenIdConnectConfiguration>(authority.TrimEnd('/') + "/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever(clients.CreateClient("OidcMetadata"))
                { RequireHttps = !new Uri(authority).IsLoopback });
        })!;
}

public sealed class DynamicOidcHandler : OpenIdConnectHandler
{
    public DynamicOidcHandler(IOptionsFactory<OpenIdConnectOptions> factory, ILoggerFactory logger, HtmlEncoder htmlEncoder, UrlEncoder encoder,
        OidcSettings settings, OidcMetadataCache metadata) : base(new RequestOptions(factory, settings, metadata), logger, htmlEncoder, encoder) { }

    private sealed class RequestOptions(IOptionsFactory<OpenIdConnectOptions> factory, OidcSettings settings, OidcMetadataCache metadata)
        : IOptionsMonitor<OpenIdConnectOptions>
    {
        private OpenIdConnectOptions? _options;
        public OpenIdConnectOptions CurrentValue => Get(OidcAuthentication.Scheme);
        public IDisposable? OnChange(Action<OpenIdConnectOptions, string?> listener) => null;
        public OpenIdConnectOptions Get(string? name)
        {
            if (_options is not null) return _options;
            var options = factory.Create(name ?? OidcAuthentication.Scheme);
            if (settings.Enabled)
            {
                options.Authority = settings.Authority;
                options.ClientId = settings.ClientId;
                options.TokenValidationParameters.ValidAudience = settings.ClientId;
                options.ClientSecret = settings.ClientSecret;
                options.RequireHttpsMetadata = !new Uri(settings.Authority!).IsLoopback;
                // Explicit custom configuration managers (including isolated protocol hosts) remain supported.
                if (options.ConfigurationManager is ConfigurationManager<OpenIdConnectConfiguration>)
                    options.ConfigurationManager = metadata.Get(settings.Authority!);
            }
            return _options = options;
        }
    }
}
