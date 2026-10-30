using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Application.Auth;
using OpenFarm.Application.Library;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Api.IntegrationTests.Library;

public class LibraryContentsEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    public sealed record Entry(Guid Id, string Name, string Kind, JsonElement? Part, JsonElement? Assembly);
    public sealed record Ancestor(Guid Id, string Name);
    public sealed record Contents(List<Entry> Items, int TotalCount, List<Ancestor> Ancestors);
    private static Uri Url(string path) => new(path, UriKind.Relative);
    private async Task<HttpClient> ClientAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(Url("/api/v1/auth/login"), new LoginCommand("admin", "admin-openfarm"));
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }
    private static Part MakePart(string name, Guid? folderId)
    {
        var part = Part.Create(name, PartFileFormat.Stl, true, "part.stl", "/tmp/part.stl", 100, 0.4m, null, null,
            EstimateStatus.Available, 60, 20, 1, 1, Guid.NewGuid().ToString(), [], [new ThumbnailSpec(0, [1, 2, 3])], []);
        part.MoveToFolder(folderId);
        return part;
    }
    [Theory]
    [InlineData("parts")]
    [InlineData("assemblies")]
    public async Task Contents_CountSortAndPageTheCombinedCollection(string library)
    {
        using var client = await ClientAsync();
        Guid parentId;
        using (var scope = factory.Services.CreateScope())
        {
            var folders = scope.ServiceProvider.GetRequiredService<ILibraryFolderService>();
            var parent = await folders.CreateAsync(library, Guid.NewGuid().ToString(), null, default);
            parentId = parent.Id;
            foreach (var name in new[] { "Zulu", "Bravo", "delta" }) await folders.CreateAsync(library, name, parentId, default);
            var db = scope.ServiceProvider.GetRequiredService<OpenFarmDbContext>();
            var dependency = MakePart("Dependency", null);
            db.Parts.Add(dependency);
            foreach (var name in new[] { "Zulu item", "alpha item", "Bravo item" })
            {
                if (library == "parts") db.Parts.Add(MakePart(name, parentId));
                else
                {
                    var assembly = Assembly.Create(name, null, [new BomLineSpec(BomLineTargetKind.Part, dependency.Id, null, null, 1)]);
                    assembly.MoveToFolder(parentId); db.Assemblies.Add(assembly);
                }
            }
            // A record in another location must not affect the count or page.
            db.Parts.Add(MakePart("Elsewhere", null));
            await db.SaveChangesAsync();
        }
        var path = $"/api/v1/library/{library}/contents?parentId={parentId}&pageSize=2&sortBy=name&sortDirection=asc";
        var first = await client.GetFromJsonAsync<Contents>(Url(path));
        first!.TotalCount.Should().Be(6);
        first.Items.Select(e => e.Name).Should().Equal("Bravo", "delta");
        first.Items.Should().OnlyContain(e => e.Kind == "folder");
        first.Ancestors.Should().ContainSingle().Which.Id.Should().Be(parentId);
        var second = await client.GetFromJsonAsync<Contents>(Url(path + "&page=2&includePreviews=true"));
        second!.Items.Select(e => e.Name).Should().Equal("Zulu", "alpha item");
        var item = second.Items[1];
        if (library == "parts") item.Part!.Value.GetProperty("thumbnailBase64").GetString().Should().Be("AQID");
        else item.Assembly!.Value.GetProperty("previewThumbnails").GetArrayLength().Should().Be(1);
        var last = await client.GetFromJsonAsync<Contents>(Url(path + "&page=3"));
        last!.Items.Select(e => e.Name).Should().Equal("Bravo item", "Zulu item");
        var descending = await client.GetFromJsonAsync<Contents>(Url(path.Replace("asc", "desc", StringComparison.Ordinal)));
        descending!.Items.Select(e => e.Name).Should().Equal("Zulu", "delta");
    }
    [Fact]
    public async Task Contents_EmptyFolderAndInvalidLocationsAreExplicit()
    {
        using var client = await ClientAsync();
        Guid id;
        using (var scope = factory.Services.CreateScope()) id = (await scope.ServiceProvider.GetRequiredService<ILibraryFolderService>().CreateAsync("parts", Guid.NewGuid().ToString(), null, default)).Id;
        var empty = await client.GetFromJsonAsync<Contents>(Url($"/api/v1/library/parts/contents?parentId={id}"));
        empty!.Items.Should().BeEmpty(); empty.TotalCount.Should().Be(0);
        (await client.GetAsync(Url($"/api/v1/library/assemblies/contents?parentId={id}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(Url($"/api/v1/library/parts/contents?parentId={Guid.NewGuid()}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(Url($"/api/v1/library/parts/contents?parentId={id}&sortBy=unknown"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
