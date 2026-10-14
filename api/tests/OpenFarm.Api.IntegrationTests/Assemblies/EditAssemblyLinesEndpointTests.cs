using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.IntegrationTests.Production;

namespace OpenFarm.Api.IntegrationTests.Assemblies;

// Test-after: PUT /api/v1/assemblies/{id} with `lines` — full BOM replace in one all-or-nothing save.
public class EditAssemblyLinesEndpointTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private static async Task<AssemblyResponse> CreateAsync(HttpClient client, string name, params (string Kind, Guid Id)[] targets)
    {
        var response = await client.PostAsJsonAsync("/api/v1/assemblies", new
        {
            name,
            description = (string?)null,
            lines = targets.Select(t => new { targetKind = t.Kind, targetId = t.Id, quantity = 1 }).ToArray(),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AssemblyResponse>())!;
    }

    [Fact]
    public async Task Edit_KeepsUpdatesRemovesAndAddsLines_AndLogsAssemblyEdited()
    {
        var client = await ClientAsync();
        var (kept, dropped, added) = ((await PartAsync()).Id, (await PartAsync()).Id, (await PartAsync()).Id);
        var assembly = await CreateAsync(client, "Before", ("Part", kept), ("Part", dropped));
        var keptLine = assembly.Lines.Single(l => l.TargetId == kept);

        var response = await client.PutAsJsonAsync($"/api/v1/assemblies/{assembly.Id}", new
        {
            name = "After",
            description = "d",
            lines = new object[]
            {
                new { lineId = keptLine.Id, targetKind = "Part", targetId = kept, quantity = 5 },
                new { targetKind = "Part", targetId = added, quantity = 2 },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = (await response.Content.ReadFromJsonAsync<AssemblyResponse>())!;
        body.Name.Should().Be("After");
        body.Lines.Should().HaveCount(2);
        body.Lines.Should().ContainSingle(l => l.Id == keptLine.Id && l.Quantity == 5);
        body.Lines.Should().ContainSingle(l => l.TargetId == added && l.Quantity == 2);
        (await ActivityEventTypesAsync(client, assembly.Id)).Should().Contain("AssemblyEdited");
    }

    [Fact]
    public async Task Edit_ReplacingEveryLine_Succeeds()
    {
        var client = await ClientAsync();
        var (oldPart, newPart) = ((await PartAsync()).Id, (await PartAsync()).Id);
        var assembly = await CreateAsync(client, "Solo", ("Part", oldPart));

        var response = await client.PutAsJsonAsync($"/api/v1/assemblies/{assembly.Id}", new
        {
            name = "Solo",
            lines = new[] { new { targetKind = "Part", targetId = newPart, quantity = 1 } },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<AssemblyResponse>())!.Lines.Should().ContainSingle(l => l.TargetId == newPart);
    }

    [Fact]
    public async Task Edit_EmptyLines_Returns400()
    {
        var client = await ClientAsync();
        var assembly = await CreateAsync(client, "Has lines", ("Part", (await PartAsync()).Id));

        var response = await client.PutAsJsonAsync($"/api/v1/assemblies/{assembly.Id}", new { name = "x", lines = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Edit_LineThatClosesACycle_Returns422AndPersistsNothing()
    {
        var client = await ClientAsync();
        var partId = (await PartAsync()).Id;
        var child = await CreateAsync(client, "Child", ("Part", partId));
        var parent = await CreateAsync(client, "Parent", ("Assembly", child.Id));
        var childLine = child.Lines.Single();

        var response = await client.PutAsJsonAsync($"/api/v1/assemblies/{child.Id}", new
        {
            name = "Child renamed",
            lines = new object[]
            {
                new { lineId = childLine.Id, targetKind = "Part", targetId = partId, quantity = 7 },
                new { targetKind = "Assembly", targetId = parent.Id, quantity = 1 },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var after = (await client.GetFromJsonAsync<AssemblyResponse>($"/api/v1/assemblies/{child.Id}"))!;
        after.Name.Should().Be("Child");
        after.Lines.Should().ContainSingle(l => l.Id == childLine.Id && l.Quantity == 1);
    }
}
