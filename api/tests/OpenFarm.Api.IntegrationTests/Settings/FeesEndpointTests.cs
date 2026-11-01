using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Settings;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.IntegrationTests.Settings;

// Test-after: fee profiles, assembly pricing and the failure allowance are stored values; the math is web-side.
public sealed class FeesEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly Uri Fees = new("/api/v1/settings/fees", UriKind.Relative);

    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new LoginCommand("admin", "admin-openfarm"));
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    [Fact]
    public async Task ReplacesTheWholeListAndRejectsInvalidProfiles()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync(Fees)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var client = await ClientAsync();

        (await client.PutAsJsonAsync(Fees, new
        {
            profiles = new object[]
            {
                new
                {
                    name = "Shopee",
                    fees = new[]
                    {
                        new { name = "Commission", type = "Percent", amount = 17.28m },
                        new { name = "Payment", type = "Percent", amount = 3.78m },
                        new { name = "Offsite ads", type = "Percent", amount = 10m },
                        new { name = "Listing fee", type = "Fixed", amount = 0.54m },
                    },
                },
                new { name = "Direct", fees = Array.Empty<object>() },
            }
        })).EnsureSuccessStatusCode();
        var saved = await client.GetFromJsonAsync<FeesResponse>(Fees);
        saved!.Profiles.Select(p => p.Name).Should().Equal("Direct", "Shopee");
        saved.Profiles[1].Fees.Select(f => (f.Name, f.Type, f.Amount)).Should().Equal(
            ("Commission", "Percent", 17.28m), ("Payment", "Percent", 3.78m), ("Offsite ads", "Percent", 10m), ("Listing fee", "Fixed", 0.54m));

        // Keeping a name across saves must not collide with the unique index.
        (await client.PutAsJsonAsync(Fees, new { profiles = new[] { new { name = "Shopee", fees = new[] { new { name = "Commission", type = "Percent", amount = 20m } } } } }))
            .EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<FeesResponse>(Fees))!.Profiles
            .Should().ContainSingle(p => p.Name == "Shopee" && p.Fees.Single().Amount == 20m);

        foreach (var invalid in new object[]
        {
            new { profiles = new[] { new { name = "A", fees = new[] { new { name = "X", type = "Percent", amount = 100m } } } } },
            new { profiles = new[] { new { name = "A", fees = new[] { new { name = "X", type = "Percent", amount = 60m }, new { name = "Y", type = "Percent", amount = 40m } } } } },
            new { profiles = new[] { new { name = "A", fees = new[] { new { name = "X", type = "Fixed", amount = -1m } } } } },
            new { profiles = new[] { new { name = "A", fees = new[] { new { name = "", type = "Fixed", amount = 1m } } } } },
            new { profiles = new[] { new { name = "A", fees = new[] { new { name = "X", type = "Monthly", amount = 1m } } } } },
            new { profiles = new[] { new { name = "", fees = Array.Empty<object>() } } },
            new { profiles = new[] { new { name = "Shop", fees = Array.Empty<object>() }, new { name = "shop ", fees = Array.Empty<object>() } } },
        })
            (await client.PutAsJsonAsync(Fees, invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetFromJsonAsync<FeesResponse>(Fees))!.Profiles.Should().ContainSingle();
    }

    [Fact]
    public async Task ProfilesSaveIndependentlyKeepIdsAndRejectDuplicateNames()
    {
        using var client = await ClientAsync();
        (await client.PutAsJsonAsync(Fees, new { profiles = Array.Empty<object>() })).EnsureSuccessStatusCode();
        var collection = new Uri("/api/v1/settings/fees/profiles", UriKind.Relative);
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync(collection, new { name = "Anonymous", fees = Array.Empty<object>() }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var directResponse = await client.PostAsJsonAsync(collection, new { name = "Direct", fees = Array.Empty<object>() });
        directResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var direct = (await directResponse.Content.ReadFromJsonAsync<FeeProfileDto>())!;
        var shop = (await (await client.PostAsJsonAsync(collection, new { name = "Shop", fees = new[] { new { name = "Commission", type = "Percent", amount = 10m } } }))
            .Content.ReadFromJsonAsync<FeeProfileDto>())!;
        var shopUri = new Uri($"/api/v1/settings/fees/profiles/{shop.Id}", UriKind.Relative);
        var updatedResponse = await client.PutAsJsonAsync(shopUri, new { name = "Shop updated", fees = new[] { new { name = "Listing", type = "Fixed", amount = 0.5m } } });
        updatedResponse.EnsureSuccessStatusCode();
        var updated = (await updatedResponse.Content.ReadFromJsonAsync<FeeProfileDto>())!;
        updated.Id.Should().Be(shop.Id);
        updated.Fees.Single().Amount.Should().Be(0.5m);
        (await client.GetFromJsonAsync<FeesResponse>(Fees))!.Profiles.Should().Contain(p => p.Id == direct.Id && p.Name == "Direct" && p.Fees.Count == 0);
        (await client.PostAsJsonAsync(collection, new { name = " direct ", fees = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(shopUri, new { name = "DIRECT", fees = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(shopUri, new { name = "Invalid", fees = new[] { new { name = "A", type = "Percent", amount = 60m }, new { name = "B", type = "Percent", amount = 40m } } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var missing = new Uri($"/api/v1/settings/fees/profiles/{Guid.NewGuid()}", UriKind.Relative);
        (await client.PutAsJsonAsync(missing, new { name = "Missing", fees = Array.Empty<object>() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync(missing)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Existing collection clients can continue replacing the list while retaining supplied IDs.
        (await client.PutAsJsonAsync(Fees, new { profiles = new[] { direct, updated } })).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<FeesResponse>(Fees))!.Profiles.Select(p => p.Id).Should().BeEquivalentTo([direct.Id, shop.Id]);
        (await client.DeleteAsync(shopUri)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<FeesResponse>(Fees))!.Profiles.Should().ContainSingle(p => p.Id == direct.Id);
    }

    [Fact]
    public async Task FailureAllowanceIsKeptWhenOmitted()
    {
        using var client = await ClientAsync();
        var uri = new Uri("/api/v1/settings/general", UriKind.Relative);
        (await client.PutAsJsonAsync(uri, new { currencyCode = "MYR", failureAllowancePercent = 10m })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(uri, new { currencyCode = "MYR" })).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<GeneralSettingsResponse>(uri))!.FailureAllowancePercent.Should().Be(10m);
        (await client.PutAsJsonAsync(uri, new { currencyCode = "MYR", failureAllowancePercent = 100m })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync(uri, new { currencyCode = "USD", failureAllowancePercent = 0m })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AssemblyPricingRoundTripsKeepsWhenOmittedAndClearsOnNull()
    {
        using var client = await ClientAsync();
        Guid partId;
        using (var scope = factory.Services.CreateScope())
        {
            var part = Part.Create(
                $"Cover {Guid.NewGuid():N}", PartFileFormat.Stl, true, "part.stl", $"/tmp/{Guid.NewGuid():N}.stl", 1024, 0.4m,
                null, null, EstimateStatus.Available, 20, 12, 1, 1, $"{Guid.NewGuid():N}", [new FilamentRequirementSpec("PETG", null, null, null, null)], [], []);
            await scope.ServiceProvider.GetRequiredService<IPartRepository>().AddAsync(part, CancellationToken.None);
            partId = part.Id;
        }

        var lines = new[] { new { targetKind = "Part", targetId = partId, quantity = 1 } };
        var created = await (await client.PostAsJsonAsync(new Uri("/api/v1/assemblies", UriKind.Relative),
            new { name = "Port cover", lines, pricing = new { mode = "Price", value = 27.49m } }))
            .Content.ReadFromJsonAsync<AssemblyResponse>();
        created!.Pricing!.Mode.Should().Be("Price");
        created.Pricing.Value.Should().Be(27.49m);
        var uri = new Uri($"/api/v1/assemblies/{created.Id}", UriKind.Relative);

        var renamed = await (await client.PutAsJsonAsync(uri, new { name = "Renamed" })).Content.ReadFromJsonAsync<AssemblyResponse>();
        renamed!.Pricing!.Value.Should().Be(27.49m);

        var margin = await (await client.PutAsJsonAsync(uri, new { name = "Renamed", pricing = new { mode = "Margin", value = 45m } }))
            .Content.ReadFromJsonAsync<AssemblyResponse>();
        margin!.Pricing!.Mode.Should().Be("Margin");

        (await client.PutAsJsonAsync(uri, new { name = "Renamed", pricing = new { mode = "Margin", value = 100m } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var cleared = await (await client.PutAsJsonAsync(uri, new { name = "Renamed", pricing = (object?)null }))
            .Content.ReadFromJsonAsync<AssemblyResponse>();
        cleared!.Pricing.Should().BeNull();
    }
}
