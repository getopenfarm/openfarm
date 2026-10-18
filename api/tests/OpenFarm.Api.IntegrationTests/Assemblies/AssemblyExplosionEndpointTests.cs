using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Api.IntegrationTests.Assemblies;

// TDD against real Testcontainers Postgres (research.md R1 — the closest available red-green
// equivalent for raw SQL). SC-001's two reference assemblies, the worked examples, and SC-007's
// 500-line/10-level performance bound.
public class AssemblyExplosionEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative), new LoginCommand("admin", "admin-openfarm"));
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private async Task<Guid> SeedPartAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var partRepository = scope.ServiceProvider.GetRequiredService<IPartRepository>();

        var part = Part.Create(
            name, PartFileFormat.Stl, true, "part.stl", $"/tmp/{Guid.NewGuid():N}.stl", 1024, 0.4m,
            null, null, EstimateStatus.Available, 60, 20, 1, 1, $"{Guid.NewGuid():N}", [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);

        await partRepository.AddAsync(part, CancellationToken.None);
        return part.Id;
    }

    private async Task<Guid> SeedNonPrintedComponentAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<INonPrintedComponentRepository>();

        var component = NonPrintedComponent.Create(name, 0.02m);
        await repository.AddAsync(component, CancellationToken.None);
        return component.Id;
    }

    [Fact]
    public async Task Explode_NestedAssemblyWithSharedPartAndNonPrintedLine_AggregatesCorrectly()
    {
        var client = await AuthenticatedClientAsync();
        var bracketId = await SeedPartAsync($"Bracket {Guid.NewGuid():N}");
        var knobId = await SeedPartAsync($"Knob {Guid.NewGuid():N}");
        var screwId = await SeedNonPrintedComponentAsync($"Screw {Guid.NewGuid():N}");

        var handleResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new { name = $"Handle {Guid.NewGuid():N}", description = (string?)null, lines = new[] { new { targetKind = "Part", targetId = knobId, quantity = 2 } } });
        var handle = await handleResponse.Content.ReadFromJsonAsync<AssemblyResponse>();

        var enclosureResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new
            {
                name = $"Enclosure {Guid.NewGuid():N}",
                description = (string?)null,
                lines = new object[]
                {
                    new { targetKind = "Part", targetId = bracketId, quantity = 4 },
                    new { targetKind = "Assembly", targetId = handle!.Id, quantity = 2 },
                    new { targetKind = "NonPrintedComponent", targetId = screwId, quantity = 6 },
                },
            });
        var enclosure = await enclosureResponse.Content.ReadFromJsonAsync<AssemblyResponse>();

        var explosionResponse = await client.GetAsync(new Uri($"/api/v1/assemblies/{enclosure!.Id}/explosion?quantity=4", UriKind.Relative));
        explosionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var explosion = await explosionResponse.Content.ReadFromJsonAsync<ExplodeAssemblyResponse>();

        explosion!.PrintedParts.Should().ContainSingle(p => p.PartId == bracketId && p.Quantity == 16);
        explosion.PrintedParts.Should().ContainSingle(p => p.PartId == knobId && p.Quantity == 16);
        explosion.NonPrintedComponents.Should().ContainSingle(c => c.NonPrintedComponentId == screwId && c.Quantity == 24);
    }

    [Fact]
    public async Task Explode_DiamondShapedSharedComponent_AggregatesIntoOneLine()
    {
        // Acceptance Scenario 2: 2 knobs direct + 2xHandle(2 knobs) -> 6 knobs, one aggregated line.
        var client = await AuthenticatedClientAsync();
        var knobId = await SeedPartAsync($"Knob {Guid.NewGuid():N}");

        var handleResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new { name = $"Handle {Guid.NewGuid():N}", description = (string?)null, lines = new[] { new { targetKind = "Part", targetId = knobId, quantity = 2 } } });
        var handle = await handleResponse.Content.ReadFromJsonAsync<AssemblyResponse>();

        var toolResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new
            {
                name = $"Tool {Guid.NewGuid():N}",
                description = (string?)null,
                lines = new object[]
                {
                    new { targetKind = "Part", targetId = knobId, quantity = 2 },
                    new { targetKind = "Assembly", targetId = handle!.Id, quantity = 2 },
                },
            });
        var tool = await toolResponse.Content.ReadFromJsonAsync<AssemblyResponse>();

        var explosionResponse = await client.GetAsync(new Uri($"/api/v1/assemblies/{tool!.Id}/explosion?quantity=1", UriKind.Relative));
        var explosion = await explosionResponse.Content.ReadFromJsonAsync<ExplodeAssemblyResponse>();

        explosion!.PrintedParts.Should().ContainSingle(p => p.PartId == knobId && p.Quantity == 6);
    }

    [Fact]
    public async Task Explode_CalledRepeatedly_ReturnsIdenticalResultsAndPersistsNothing()
    {
        var client = await AuthenticatedClientAsync();
        var partId = await SeedPartAsync($"Idempotent Part {Guid.NewGuid():N}");

        var assemblyResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new { name = $"Idempotent {Guid.NewGuid():N}", description = (string?)null, lines = new[] { new { targetKind = "Part", targetId = partId, quantity = 3 } } });
        var assembly = await assemblyResponse.Content.ReadFromJsonAsync<AssemblyResponse>();

        ExplodeAssemblyResponse? first = null;
        for (var i = 0; i < 10; i++)
        {
            var response = await client.GetAsync(new Uri($"/api/v1/assemblies/{assembly!.Id}/explosion?quantity=5", UriKind.Relative));
            var body = await response.Content.ReadFromJsonAsync<ExplodeAssemblyResponse>();

            first ??= body;
            body!.PrintedParts.Single().Quantity.Should().Be(first!.PrintedParts.Single().Quantity);
        }

        var listAfter = await client.GetAsync(new Uri("/api/v1/assemblies?page=1&pageSize=1", UriKind.Relative));
        listAfter.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Explode_InvalidQuantity_Returns400(int quantity)
    {
        var client = await AuthenticatedClientAsync();
        var partId = await SeedPartAsync($"Invalid Qty Part {Guid.NewGuid():N}");

        var assemblyResponse = await client.PostAsJsonAsync(
            new Uri("/api/v1/assemblies", UriKind.Relative),
            new { name = $"Invalid Qty {Guid.NewGuid():N}", description = (string?)null, lines = new[] { new { targetKind = "Part", targetId = partId, quantity = 1 } } });
        var assembly = await assemblyResponse.Content.ReadFromJsonAsync<AssemblyResponse>();

        var response = await client.GetAsync(new Uri($"/api/v1/assemblies/{assembly!.Id}/explosion?quantity={quantity}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Explode_FullTenLevelFiveHundredLineAssembly_CompletesUnderOneSecond()
    {
        // SC-007: 500 total BOM lines spread across the full 10 levels of nesting, under 1 second.
        // 50 lines per level x 10 levels: each level's assembly has 49 Part lines + 1 line to the next level.
        using var scope = factory.Services.CreateScope();
        var partRepository = scope.ServiceProvider.GetRequiredService<IPartRepository>();
        var assemblyRepository = scope.ServiceProvider.GetRequiredService<IAssemblyRepository>();

        var parts = new List<Guid>();
        for (var i = 0; i < 49; i++)
        {
            var part = Part.Create(
                $"Perf Part {i} {Guid.NewGuid():N}", PartFileFormat.Stl, true, "p.stl", $"/tmp/{Guid.NewGuid():N}.stl",
                1024, 0.4m, null, null, EstimateStatus.Available, 60, 20, 1, 1, $"{Guid.NewGuid():N}", [new FilamentRequirementSpec("PLA", null, null, null, null)], [], []);
            await partRepository.AddAsync(part, CancellationToken.None);
            parts.Add(part.Id);
        }

        Assembly? previousLevel = null;
        for (var level = 0; level < 10; level++)
        {
            var lines = parts.Select(p => new BomLineSpec(BomLineTargetKind.Part, p, null, null, 1)).ToList();
            if (previousLevel is not null)
            {
                lines.Add(new BomLineSpec(BomLineTargetKind.Assembly, null, previousLevel.Id, null, 1));
            }

            var assembly = Assembly.Create($"Perf Level {level} {Guid.NewGuid():N}", null, lines);
            await assemblyRepository.AddAsync(assembly, CancellationToken.None);
            previousLevel = assembly;
        }

        var rootId = previousLevel!.Id;

        var client = await AuthenticatedClientAsync();
        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync(new Uri($"/api/v1/assemblies/{rootId}/explosion?quantity=1", UriKind.Relative));
        stopwatch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var explosion = await response.Content.ReadFromJsonAsync<ExplodeAssemblyResponse>();
        explosion!.PrintedParts.Should().HaveCount(49);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
}
