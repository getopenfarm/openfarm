using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace OpenFarm.Api.IntegrationTests.Hosting;

public sealed class SpaHostingTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task ProductionSpa_ServesClientRoutesAndCachesAssets_WithoutMaskingServerErrors()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "openfarm-spa-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(webRoot, "assets"));
        const string shell = "<!doctype html><html>OpenFarm test shell</html>";
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), shell);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "assets", "index-aB12_cD3.js"), "console.log('test');");
        await File.WriteAllTextAsync(Path.Combine(webRoot, "assets", "unversioned.js"), "console.log('unversioned');");
        await File.WriteAllTextAsync(Path.Combine(webRoot, "favicon.svg"), "<svg></svg>");

        try
        {
            await using var app = factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseWebRoot(webRoot);
            });
            using var client = app.CreateClient();
            foreach (var path in new[] { "/", "/index.html", "/printers/example" })
            {
                using var response = await client.GetAsync(path);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                (await response.Content.ReadAsStringAsync()).Should().Be(shell);
                response.Headers.CacheControl!.NoCache.Should().BeTrue();
                using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
                head.StatusCode.Should().Be(HttpStatusCode.OK);
                head.Headers.CacheControl!.NoCache.Should().BeTrue();
                (await head.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
            }

            using var asset = await client.GetAsync("/assets/index-aB12_cD3.js");
            asset.StatusCode.Should().Be(HttpStatusCode.OK);
            asset.Content.Headers.ContentType!.MediaType.Should().BeOneOf("text/javascript", "application/javascript");
            asset.Headers.CacheControl!.Public.Should().BeTrue();
            asset.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365));
            asset.Headers.CacheControl.Extensions.Should().Contain(x => x.Name == "immutable");

            using var assetHead = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/assets/index-aB12_cD3.js"));
            assetHead.StatusCode.Should().Be(HttpStatusCode.OK);
            assetHead.Headers.CacheControl!.Extensions.Should().Contain(x => x.Name == "immutable");
            (await assetHead.Content.ReadAsByteArrayAsync()).Should().BeEmpty();

            foreach (var path in new[] { "/favicon.svg", "/assets/unversioned.js" })
            {
                using var response = await client.GetAsync(path);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                response.Headers.CacheControl!.NoCache.Should().BeTrue();
            }

            foreach (var path in new[] { "/api", "/api/v1/missing", "/hubs", "/hubs/missing", "/health", "/health/missing", "/assets", "/assets/missing", "/assets/missing.js", "/missing.css" })
            {
                foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
                {
                    using var response = await client.SendAsync(new HttpRequestMessage(method, path));
                    response.StatusCode.Should().Be(HttpStatusCode.NotFound, "{0} {1} is not a client route", method, path);
                    (await response.Content.ReadAsStringAsync()).Should().NotContain(shell);
                }
            }

            using var post = await client.PostAsync("/printers/example", new StringContent(""));
            post.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
            (await post.Content.ReadAsStringAsync()).Should().NotContain(shell);
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }
}
