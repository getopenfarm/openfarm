using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenFarm.Api.Security;

namespace OpenFarm.Api.IntegrationTests.Auth;

public sealed class OidcMetadataTests
{
    [Fact]
    public async Task DiscoveryAndSigningKeys_AreRetrievedThroughAnEdgeThatRejectsMissingUserAgents()
    {
        const string issuer = "https://identity.example/application/o/farm/";
        using var rsa = RSA.Create(2048);
        var edge = new MetadataEdge(issuer, rsa.ExportParameters(false));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication().AddExternalOidc();
        services.AddHttpClient("OidcMetadata").ConfigurePrimaryHttpMessageHandler(() => edge);
        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<OidcMetadataCache>().Get(issuer);

        var configuration = await manager.GetConfigurationAsync(CancellationToken.None);

        configuration.Issuer.Should().Be(issuer);
        configuration.SigningKeys.Should().ContainSingle(k => k.KeyId == "provider-key");
        edge.Requests.Should().Equal(issuer + ".well-known/openid-configuration", issuer + "jwks/");
        edge.UserAgents.Should().OnlyContain(agent => agent.StartsWith("OpenFarm", StringComparison.Ordinal));
    }

    private sealed class MetadataEdge(string issuer, RSAParameters key) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var address = request.RequestUri!.AbsoluteUri;
            Requests.Add(address);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            // Browser-integrity protection can reject an otherwise public metadata request.
            if (request.Headers.UserAgent.Count == 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            if (address == issuer + ".well-known/openid-configuration")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        issuer,
                        authorization_endpoint = issuer + "authorize/",
                        token_endpoint = issuer + "token/",
                        jwks_uri = issuer + "jwks/",
                    }),
                });
            if (address == issuer + "jwks/")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        keys = new[] { new { kty = "RSA", use = "sig", kid = "provider-key",
                            n = Base64UrlEncoder.Encode(key.Modulus!), e = Base64UrlEncoder.Encode(key.Exponent!) } },
                    }),
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
