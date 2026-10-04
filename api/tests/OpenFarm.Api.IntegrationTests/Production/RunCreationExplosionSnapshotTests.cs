using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Assemblies;
using OpenFarm.Api.Endpoints.ProductionRuns;
using OpenFarm.Application.Assemblies;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Organizations;

namespace OpenFarm.Api.IntegrationTests.Production;

public class RunCreationExplosionSnapshotTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    [Fact]
    public async Task NestedAssembly_AggregatesSharedLeaf_IgnoresNonPrintedParts_AndNeverReexplodes()
    {
        var client = await ClientAsync();
        var part = await PartAsync(4);
        Guid rootId;
        using (var scope = Factory.CreateTenantScope())
        {
            var assemblies = scope.ServiceProvider.GetRequiredService<IAssemblyRepository>();
            var components = scope.ServiceProvider.GetRequiredService<INonPrintedComponentRepository>();
            var screw = NonPrintedComponent.Create(Organization.WellKnownId, "Screw", "each", 1);
            await components.AddAsync(screw, default);
            var nested = Assembly.Create(Organization.WellKnownId, "Nested", null, [new BomLineSpec(BomLineTargetKind.Part, part.Id, null, null, 2)]);
            await assemblies.AddAsync(nested, default);
            var root = Assembly.Create(Organization.WellKnownId, "Root", null, [
                new BomLineSpec(BomLineTargetKind.Part, part.Id, null, null, 2),
                new BomLineSpec(BomLineTargetKind.Assembly, null, nested.Id, null, 2),
                new BomLineSpec(BomLineTargetKind.NonPrintedComponent, null, null, screw.Id, 6)]);
            await assemblies.AddAsync(root, default);
            rootId = root.Id;
        }
        var response = await client.PostAsJsonAsync("/api/v1/production-runs", new
        { lines = new[] { new { targetKind = "Assembly", targetId = rootId, quantity = 3 } } });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var run = (await response.Content.ReadFromJsonAsync<ProductionRunDetailResponse>())!;
        run.Lines.Single().Requirements.Should().ContainSingle(r => r.PartId == part.Id && r.Quantity == 18 && r.JobCount == 5);
        var edit = await client.PutAsJsonAsync($"/api/v1/assemblies/{rootId}", new
        { name = "Edited root", lines = new[] { new { targetKind = "Part", targetId = part.Id, quantity = 99 } } });
        edit.EnsureSuccessStatusCode();
        (await DetailAsync(client, run.Id)).Lines.Single().Requirements.Single().Quantity.Should().Be(18);
    }
}
