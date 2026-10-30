using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Parts;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Library;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Library;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Library;

public class FolderEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly int[] SplitPlates = [1, 2];
    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new LoginCommand("admin", "admin-openfarm"));
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }
    public sealed record FolderPageDto(List<FolderDto> Items, int TotalCount, List<FolderDto> Ancestors);
    public sealed record FolderDto(Guid Id, string Name);
    private static Uri Url(string path) => new(path, UriKind.Relative);
    private static async Task<FolderDto> FolderAsync(HttpClient client, string library, Guid? parent = null)
    {
        var response = await client.PostAsJsonAsync(Url($"/api/v1/library/{library}/folders"), new { name = Guid.NewGuid().ToString(), parentId = parent });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FolderDto>())!;
    }
    private async Task<Guid> PartAsync(string? name = null)
    {
        using var scope = factory.Services.CreateScope();
        var part = Part.Create(name ?? Guid.NewGuid().ToString(), PartFileFormat.Stl, true, "part.stl", "/tmp/part.stl", 100, 0.4m,
            null, null, EstimateStatus.Available, 60, 20, 1, 1, Guid.NewGuid().ToString(), [], [new ThumbnailSpec(0, [1, 2, 3])], []);
        await scope.ServiceProvider.GetRequiredService<IPartRepository>().AddAsync(part, default);
        return part.Id;
    }
    [Fact]
    public async Task NestedFolders_BreadcrumbsRenameMoveAndEmptyRemoval()
    {
        using var client = await ClientAsync();
        var parent = await FolderAsync(client, "parts");
        var child = await FolderAsync(client, "parts", parent.Id);
        var listing = await client.GetFromJsonAsync<FolderPageDto>(Url($"/api/v1/library/parts/folders?parentId={child.Id}"));
        listing!.Ancestors.Select(f => f.Name).Should().Equal(parent.Name, child.Name);
        (await client.DeleteAsync(Url($"/api/v1/library/parts/folders/{parent.Id}"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PatchAsJsonAsync(Url($"/api/v1/library/parts/folders/{child.Id}"), new { name = "Renamed" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(Url($"/api/v1/library/parts/folders/{child.Id}/move"), new { parentId = (Guid?)null })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync(Url($"/api/v1/library/parts/folders/{parent.Id}"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
    [Fact]
    public async Task Moves_RejectCyclesWrongLibrariesAndDuplicateSiblingNames()
    {
        using var client = await ClientAsync();
        var parent = await FolderAsync(client, "parts");
        var child = await FolderAsync(client, "parts", parent.Id);
        var otherLibrary = await FolderAsync(client, "assemblies");
        (await client.PostAsJsonAsync(Url($"/api/v1/library/parts/folders/{parent.Id}/move"), new { parentId = child.Id })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync(Url($"/api/v1/library/parts/folders/{parent.Id}/move"), new { parentId = otherLibrary.Id })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync(Url("/api/v1/library/parts/folders"), new { name = parent.Name.ToUpperInvariant() })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task ItemMoves_AreAtomicKeepCatalogCompatibilityAndPreserveBom()
    {
        using var client = await ClientAsync();
        var folder = await FolderAsync(client, "parts");
        var partId = await PartAsync();
        Guid assemblyId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var assembly = Assembly.Create("Folder BOM", null, [new BomLineSpec(BomLineTargetKind.Part, partId, null, null, 1)]);
            assemblyId = assembly.Id; db.Assemblies.Add(assembly); await db.SaveChangesAsync();
        }
        var moveUrl = Url("/api/v1/library/parts/items/move");
        (await client.PostAsJsonAsync(moveUrl, new { ids = new[] { partId }, folderId = folder.Id })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var page = await client.GetFromJsonAsync<ListPartsResponse>(Url($"/api/v1/parts?folder={folder.Id}&pageSize=1"));
        page!.TotalCount.Should().Be(1); page.Parts[0].Id.Should().Be(partId);
        var all = await client.GetFromJsonAsync<ListPartsResponse>(Url("/api/v1/parts?pageSize=200"));
        all!.Parts.Should().Contain(p => p.Id == partId);
        var root = await client.GetFromJsonAsync<ListPartsResponse>(Url("/api/v1/parts?folder=root&pageSize=200"));
        root!.Parts.Should().NotContain(p => p.Id == partId);
        (await client.PostAsJsonAsync(moveUrl, new { ids = new[] { partId, Guid.NewGuid() }, folderId = (Guid?)null })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync(Url($"/api/v1/library/parts/folders/{folder.Id}"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var check = factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        (await context.Parts.FindAsync(partId))!.FolderId.Should().Be(folder.Id);
        (await context.BomLines.FirstAsync(l => l.AssemblyId == assemblyId)).TargetPartId.Should().Be(partId);
    }
    [Fact]
    public async Task ConcurrentMoves_CannotJointlyCreateCycle()
    {
        using var client = await ClientAsync();
        var a = await FolderAsync(client, "assemblies"); var b = await FolderAsync(client, "assemblies");
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(Url($"/api/v1/library/assemblies/folders/{a.Id}/move"), new { parentId = b.Id }),
            client.PostAsJsonAsync(Url($"/api/v1/library/assemblies/folders/{b.Id}/move"), new { parentId = a.Id }));
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
    }
    [Fact]
    public async Task PreviewReader_CollectsDistinctPartsThroughNestedAssemblies()
    {
        var partId = await PartAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
        var child = Assembly.Create("Child", null, [new BomLineSpec(BomLineTargetKind.Part, partId, null, null, 1)]);
        var parent = Assembly.Create("Parent", null, [new BomLineSpec(BomLineTargetKind.Part, partId, null, null, 1), new BomLineSpec(BomLineTargetKind.Assembly, null, child.Id, null, 1)]);
        db.Assemblies.AddRange(child, parent); await db.SaveChangesAsync();
        var images = await scope.ServiceProvider.GetRequiredService<IAssemblyPreviewReader>().ReadAsync([child.Id, parent.Id], default);
        images[child.Id].Should().Equal(Convert.ToBase64String([1, 2, 3]));
        images[parent.Id].Should().Equal(images[child.Id]);
    }
    [Fact]
    public async Task CreateAssembly_InFolderAndFolderPagingKeepGlobalCatalogAvailable()
    {
        using var client = await ClientAsync();
        var folder = await FolderAsync(client, "assemblies");
        var partId = await PartAsync();
        var ids = new List<Guid>();
        foreach (var name in new[] { "Zulu", "Alpha" })
        {
            var response = await client.PostAsJsonAsync(Url("/api/v1/assemblies"), new { name, folderId = folder.Id, lines = new[] { new { targetKind = "Part", targetId = partId, quantity = 1 } } });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var body = await response.Content.ReadFromJsonAsync<OpenFarm.Api.Endpoints.Assemblies.AssemblyResponse>();
            ids.Add(body!.Id);
        }
        var page = await client.GetFromJsonAsync<OpenFarm.Api.Endpoints.Assemblies.ListAssembliesResponse>(Url($"/api/v1/assemblies?folder={folder.Id}&page=1&pageSize=1&sortBy=name&sortDirection=asc&includePreviews=true"));
        page!.TotalCount.Should().Be(2);
        page.Items.Should().ContainSingle().Which.Name.Should().Be("Alpha");
        page.Items[0].PreviewThumbnails.Should().ContainSingle();
        var all = await client.GetFromJsonAsync<OpenFarm.Api.Endpoints.Assemblies.ListAssembliesResponse>(Url("/api/v1/assemblies?pageSize=200"));
        all!.Items.Select(i => i.Id).Should().Contain(ids);
        (await client.PostAsJsonAsync(Url("/api/v1/library/parts/items/move"), new { ids = new[] { partId }, folderId = folder.Id })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    [Fact]
    public async Task ConfirmSplitParts_AssignsEveryPlateToTheDestinationFolder()
    {
        using var client = await ClientAsync();
        var folder = await FolderAsync(client, "parts");
        var file = OpenFarm.Api.IntegrationTests.Parts.OptionalPartMetadataTests.Zip(
            ("Metadata/plate_1.gcode", "; nozzle_diameter = 0.4\n; filament_type = PLA\nG28\nG1 X10 Y10\n"),
            ("Metadata/plate_2.gcode", "; nozzle_diameter = 0.4\n; filament_type = PLA\nG28\nG1 X10 Y10\n"));
        var submitted = await OpenFarm.Api.IntegrationTests.Parts.OptionalPartMetadataTests.SubmitAsync(client, "/api/v1/parts", "kit.gcode.3mf", file);
        await OpenFarm.Api.IntegrationTests.Parts.OptionalPartMetadataTests.WaitAsync(client, submitted.JobId);
        var response = await client.PostAsJsonAsync(Url($"/api/v1/part-processing-jobs/{submitted.JobId}/confirm"), new { name = "Folder kit", folderId = folder.Id, plateNumbers = SplitPlates });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var page = await client.GetFromJsonAsync<ListPartsResponse>(Url($"/api/v1/parts?folder={folder.Id}"));
        page!.Parts.Should().HaveCount(2);
        page.Parts.Should().OnlyContain(p => p.Name.StartsWith("Folder kit", StringComparison.Ordinal));
    }

}
