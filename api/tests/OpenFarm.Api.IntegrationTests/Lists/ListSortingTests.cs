using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;
using System.Text.Json;
using FluentAssertions;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.IntegrationTests.Production;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Production;
using OpenFarm.Domain.Profiles;
using OpenFarm.Infrastructure.Multitenancy;
using OpenFarm.Infrastructure.Persistence;
using Assembly = OpenFarm.Domain.Assemblies.Assembly;

namespace OpenFarm.Api.IntegrationTests.Lists;

public sealed class ListSortingTests(ProductionTestFactory factory) : ProductionTestContext(factory), IClassFixture<ProductionTestFactory>
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CustomWebApplicationFactory, LoginResponse> Tokens = new();

    private new async Task<HttpClient> ClientAsync()
    {
        var client = Factory.CreateClient();
        if (!Tokens.TryGetValue(Factory, out var result))
        {
            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin", "admin-openfarm"));
            login.EnsureSuccessStatusCode();
            result = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
            Tokens.Add(Factory, result);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Token);
        await Factory.EnsureTenantInfoResolvedAsync();
        return client;
    }

    private static Part Part(string name, IReadOnlyList<Tag>? tags = null) => OpenFarm.Domain.Parts.Part.Create(
        Organization.WellKnownId, name, PartFileFormat.GCode, false, "part.gcode", "unused.gcode", 10,
        0.4m, null, null, EstimateStatus.Available, 60, 20, 1, 1, Guid.NewGuid().ToString("N"),
        [new FilamentRequirementSpec("PLA", null, null, null, null)], [], tags ?? []);

    private static RunLineSpec Line(Guid target, int? quantity = 1, RunLineTargetKind kind = RunLineTargetKind.Part) =>
        new(kind, target, quantity, null, AssignmentMode.Unassigned, null, 1, null);

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static async Task AssertPagesAsync(HttpClient client, string url, string collection, string field, string sortField)
    {
        var all = await GetAsync(client, $"{url}&page=1&pageSize=200");
        var rows = all.GetProperty(collection).EnumerateArray().ToList();
        rows.Count.Should().BeGreaterThan(2);
        foreach (var descending in new[] { false, true })
        {
            var expected = (descending
                    ? rows.OrderByDescending(x => x.GetProperty(field).GetString(), StringComparer.OrdinalIgnoreCase)
                    : rows.OrderBy(x => x.GetProperty(field).GetString(), StringComparer.OrdinalIgnoreCase))
                .ThenBy(x => x.GetProperty("id").GetGuid()).Select(x => x.GetProperty("id").GetGuid()).ToList();
            var actual = new List<Guid>();
            for (var page = 1; actual.Count < expected.Count; page++)
            {
                var result = await GetAsync(client, $"{url}&sortBy={sortField}&sortDirection={(descending ? "desc" : "asc")}&page={page}&pageSize=2");
                result.GetProperty("totalCount").GetInt32().Should().Be(expected.Count);
                var ids = result.GetProperty(collection).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
                ids.Should().NotBeEmpty();
                actual.AddRange(ids);
            }
            actual.Should().Equal(expected);
            actual.Should().OnlyHaveUniqueItems();
        }
        var beyond = await GetAsync(client, $"{url}&sortBy={sortField}&page=999&pageSize=10");
        beyond.GetProperty(collection).GetArrayLength().Should().Be(0);
        beyond.GetProperty("totalCount").GetInt32().Should().Be(rows.Count);
    }

    [Fact]
    public async Task ResourceLists_SortFullCollectionsBeforePaging_WithStableTiesAndDefaults()
    {
        using var client = await ClientAsync();
        var tagName = $"sorting-{Guid.NewGuid():N}";
        var partIds = new List<Guid>();
        var assemblyIds = new List<Guid>();
        var componentIds = new List<Guid>();
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var tag = Tag.Create(Organization.WellKnownId, tagName);
            db.Tags.Add(tag);
            var timestamp = DateTimeOffset.UtcNow;
            foreach (var name in new[] { "Zulu", "alpha", "ALPHA", "Bravo" })
            {
                var part = Part(name, [tag]);
                var component = NonPrintedComponent.Create(Organization.WellKnownId, name, 1);
                var assembly = Assembly.Create(Organization.WellKnownId, name, null,
                    [new(BomLineTargetKind.NonPrintedComponent, null, null, component.Id, 1)]);
                db.AddRange(part, component, assembly);
                // Identical timestamps ensure the default ordering also needs its ID tie-breaker.
                db.Entry(part).Property(p => p.CreatedAtUtc).CurrentValue = timestamp;
                db.Entry(component).Property(p => p.CreatedAtUtc).CurrentValue = timestamp;
                db.Entry(assembly).Property(p => p.CreatedAtUtc).CurrentValue = timestamp;
                partIds.Add(part.Id); componentIds.Add(component.Id); assemblyIds.Add(assembly.Id);
            }
            db.Parts.Add(Part("Outside tag"));
            await db.SaveChangesAsync();
        }
        foreach (var (url, collection, ids) in new[] {
            ($"/api/v1/parts?tag={tagName}", "parts", partIds),
            ("/api/v1/assemblies?", "items", assemblyIds),
            ("/api/v1/non-printed-components?", "items", componentIds) })
        {
            await AssertPagesAsync(client, url, collection, "name", "name");
            var defaults = await GetAsync(client, $"{url}&pageSize=200");
            defaults.GetProperty(collection).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Where(ids.Contains)
                .Should().Equal(ids.Order());
        }
        var empty = await GetAsync(client, $"/api/v1/parts?tag=absent-{Guid.NewGuid():N}&sortBy=name&page=1&pageSize=10");
        empty.GetProperty("totalCount").GetInt32().Should().Be(0);
        empty.GetProperty("parts").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Runs_SortByDisplayedLabels_IncludingSnapshotsLegacyFallbacksAndUnlimitedLines()
    {
        using var client = await ClientAsync();
        var expected = new Dictionary<Guid, string>();
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var part = Part("Legacy bracket");
            var component = NonPrintedComponent.Create(Organization.WellKnownId, "Insert", null);
            var assembly = Assembly.Create(Organization.WellKnownId, "Legacy assembly", null,
                [new(BomLineTargetKind.NonPrintedComponent, null, null, component.Id, 1)]);
            db.AddRange(part, component, assembly);
            var named = ProductionRun.Create(Organization.WellKnownId, "Zulu named", null, null, null, [Line(part.Id)]);
            var finite = ProductionRun.Create(Organization.WellKnownId, null, null, null, null, [Line(part.Id, 20)]);
            finite.Lines[0].CaptureTargetName("alpha captured");
            var unlimited = ProductionRun.Create(Organization.WellKnownId, null, null, null, null, [Line(part.Id, null)]);
            var missing = ProductionRun.Create(Organization.WellKnownId, null, null, null, null, [Line(Guid.NewGuid(), 2)]);
            var missingAssembly = ProductionRun.Create(Organization.WellKnownId, null, null, null, null, [Line(Guid.NewGuid(), null, RunLineTargetKind.Assembly)]);
            var assemblyRun = ProductionRun.Create(Organization.WellKnownId, null, null, null, null, [Line(assembly.Id, 3, RunLineTargetKind.Assembly)]);
            var multiple = ProductionRun.Create(Organization.WellKnownId, null, null, null, null, [Line(part.Id), Line(assembly.Id, null, RunLineTargetKind.Assembly)]);
            multiple.Lines[0].CaptureTargetName("First captured");
            multiple.Lines[1].CaptureTargetName("Second captured");
            var empty = ProductionRun.Create(Organization.WellKnownId, null, null, null, null, [Line(part.Id)]);
            db.AddRange(named, finite, unlimited, missing, missingAssembly, assemblyRun, multiple, empty);
            await db.SaveChangesAsync();
            // Old persisted data can contain an empty run even though new domain creation rejects it.
            await db.RunLines.Where(l => l.ProductionRunId == empty.Id).ExecuteDeleteAsync();
            expected[named.Id] = "Zulu named";
            expected[finite.Id] = "alpha captured ×20";
            expected[unlimited.Id] = "Legacy bracket";
            expected[missing.Id] = "Unknown part ×2";
            expected[missingAssembly.Id] = "Unknown assembly";
            expected[assemblyRun.Id] = "Legacy assembly ×3";
            expected[multiple.Id] = "2 lines — " + string.Join(", ", multiple.Lines.OrderBy(l => l.Id).Select(l => l.TargetName));
            expected[empty.Id] = "Empty run";
        }
        var result = await GetAsync(client, "/api/v1/production-runs?pageSize=200&sortBy=displayLabel");
        foreach (var row in result.GetProperty("items").EnumerateArray())
            row.GetProperty("displayLabel").GetString().Should().Be(expected[row.GetProperty("id").GetGuid()]);
        await AssertPagesAsync(client, "/api/v1/production-runs?", "items", "displayLabel", "displayLabel");
    }

    [Fact]
    public async Task Profiles_SortNamesAcrossPages_AndPreserveLegacyCalls()
    {
        using var client = await ClientAsync();
        var prefix = $"Sort-{Guid.NewGuid():N}-";
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            foreach (var name in new[] { "Zulu", "alpha", "ALPHA", "Bravo" })
            {
                db.MachineProfiles.Add(MachineProfile.Create(Organization.WellKnownId, prefix + name, ProfileSourceFormat.OrcaSlicer, null, "{}"));
                db.FilamentProfiles.Add(FilamentProfile.Create(Organization.WellKnownId, prefix + name, ProfileSourceFormat.OrcaSlicer, null, "{}"));
            }
            await db.SaveChangesAsync();
        }
        foreach (var kind in new[] { "machine", "filament" })
        {
            await AssertPagesAsync(client, $"/api/v1/profiles/{kind}?origin=User&q={prefix}", "items", "name", "name");
            var legacy = await GetAsync(client, $"/api/v1/profiles/{kind}");
            legacy.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(4);
            legacy.GetProperty("page").GetInt32().Should().Be(1);
        }
    }

    [Theory]
    [InlineData("parts", "name")]
    [InlineData("assemblies", "name")]
    [InlineData("non-printed-components", "name")]
    [InlineData("production-runs", "displayLabel")]
    [InlineData("profiles/machine", "name")]
    [InlineData("profiles/filament", "name")]
    public async Task InvalidSort_Returns400(string route, string field)
    {
        using var client = await ClientAsync();
        foreach (var query in new[] { "sortBy=unsupported", $"sortBy={field}&sortDirection=sideways", "sortDirection=asc", "sortBy=" })
            (await client.GetAsync($"/api/v1/{route}?{query}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RunProjection_DoesNotReadAnotherTenantsRunsOrLegacyTargetNames()
    {
        using var client = await ClientAsync();
        Guid foreignPart;
        using (var scope = Factory.CreateTenantScope())
        {
            var part = Part("Secret foreign name");
            foreignPart = part.Id;
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            db.Parts.Add(part);
            await db.SaveChangesAsync();
        }
        using (var scope = Factory.CreateTenantScope())
        {
            var tenant = Guid.NewGuid();
            scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext =
                new MultiTenantContext<AppTenantInfo>(new AppTenantInfo { Id = tenant.ToString(), Identifier = tenant.ToString(), Name = "Other" });
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var run = ProductionRun.Create(tenant, null, null, null, null, [Line(foreignPart)]);
            var boundary = ProductionRun.Create(tenant, "T boundary", null, null, null, [Line(Guid.NewGuid())]);
            db.AddRange(run, boundary);
            await db.SaveChangesAsync();
            var page = await scope.ServiceProvider.GetRequiredService<IProductionRunRepository>()
                .ListAsync(1, 25, default, new ListSort("displayLabel", false));
            page.TotalCount.Should().Be(2);
            // A leaked foreign name would sort before T, whereas Unknown must sort after it.
            page.Runs.Select(r => r.Id).Should().Equal(boundary.Id, run.Id);
            var label = await scope.ServiceProvider.GetRequiredService<ProductionRunDisplayLabelResolver>().ResolveAsync(page.Runs[1], default);
            label.Should().Be("Unknown part ×1");
        }
    }

    [Fact]
    public async Task ActivityLog_UsesStableTimestampOrderingAcrossPages()
    {
        using var client = await ClientAsync();
        var entity = $"Sort-{Guid.NewGuid():N}";
        var ids = new List<Guid>();
        using (var scope = Factory.CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var timestamp = DateTimeOffset.UtcNow;
            for (var i = 0; i < 4; i++)
            {
                var entry = ActivityLogEntry.Create(Organization.WellKnownId, entity, i.ToString(System.Globalization.CultureInfo.InvariantCulture), "Created", null, ActorType.System);
                db.ActivityLogEntries.Add(entry);
                db.Entry(entry).Property(e => e.TimestampUtc).CurrentValue = timestamp;
                ids.Add(entry.Id);
            }
            await db.SaveChangesAsync();
        }
        var actual = new List<Guid>();
        for (var page = 1; page <= 2; page++)
        {
            var result = await GetAsync(client, $"/api/v1/activity-log?entityType={entity}&page={page}&pageSize=2");
            result.GetProperty("totalCount").GetInt32().Should().Be(4);
            actual.AddRange(result.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
        }
        actual.Should().Equal(ids.Order());
    }
}
