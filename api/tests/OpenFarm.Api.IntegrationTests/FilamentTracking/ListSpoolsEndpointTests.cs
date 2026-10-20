using System.Net.Http.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Filaments;
using OpenFarm.Api.Endpoints.Spools;
using OpenFarm.Api.Endpoints.Vendors;
using static OpenFarm.Api.IntegrationTests.FilamentTracking.FilamentTrackingTestHelpers;

namespace OpenFarm.Api.IntegrationTests.FilamentTracking;

/// <summary>Requires Docker (Testcontainers.PostgreSql) — exercised by CI. Names are GUID-tagged because the fixture's database is shared across tests.</summary>
public class ListSpoolsEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private sealed record Seeded(Guid VendorId, Guid FilamentId, Guid VariantId, string Tag);

    private static async Task<Seeded> SeedAsync(HttpClient client, string material, string materialType, string colorName)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var vendorResponse = await client.PostAsJsonAsync(new Uri("/api/v1/vendors", UriKind.Relative), new CreateVendorRequest { Name = $"Vendor{tag}" });
        var vendor = await vendorResponse.Content.ReadFromJsonAsync<VendorResponse>();

        var filamentResponse = await client.PostAsJsonAsync(new Uri("/api/v1/filaments", UriKind.Relative), new CreateFilamentRequest
        {
            VendorId = vendor!.Id,
            Material = $"{material}{tag}",
            MaterialType = materialType,
            Variants = [new CreateFilamentVariantRequest { ColorName = $"{colorName}{tag}", ColorHex = "#112233" }],
        });
        var filament = await filamentResponse.Content.ReadFromJsonAsync<FilamentResponse>();
        return new Seeded(vendor.Id, filament!.Id, filament.Variants[0].Id, tag);
    }

    private static async Task<ListSpoolsResponse> ListPageAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<ListSpoolsResponse>(new Uri($"/api/v1/spools?{query}", UriKind.Relative)))!;

    private static async Task<IReadOnlyList<SpoolResponse>> ListAsync(HttpClient client, string query) =>
        (await ListPageAsync(client, query)).Spools;

    [Fact]
    public async Task Search_MatchesVendorMaterialAndColor_CaseInsensitively_AllTermsRequired()
    {
        var client = await AuthenticatedClientAsync(_client);
        var match = await SeedAsync(client, "Silk", "PLA", "Crimson");
        var other = await SeedAsync(client, "Silk", "PLA", "Teal");
        var matchSpool = await RegisterSpoolAsync(client, match.VariantId);
        var otherSpool = await RegisterSpoolAsync(client, other.VariantId);

        var byVendor = await ListAsync(client, $"search=VENDOR{match.Tag.ToUpperInvariant()}");
        byVendor.Select(s => s.Id).Should().Equal(matchSpool);

        var byColor = await ListAsync(client, $"search=crimson{match.Tag}");
        byColor.Select(s => s.Id).Should().Equal(matchSpool);

        // Two terms are ANDed across different columns: material of one filament, color of the same filament.
        var both = await ListAsync(client, $"search=silk{match.Tag}%20crimson{match.Tag}");
        both.Select(s => s.Id).Should().Equal(matchSpool);

        var crossed = await ListAsync(client, $"search=silk{match.Tag}%20teal{other.Tag}");
        crossed.Should().BeEmpty();
        otherSpool.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Search_MatchesMaterialTypeAndSpoolLabel()
    {
        var client = await AuthenticatedClientAsync(_client);
        var seeded = await SeedAsync(client, "Matte", $"TYPE{Guid.NewGuid():N}"[..14], "Olive");
        var registered = await client.PostAsJsonAsync(new Uri("/api/v1/spools", UriKind.Relative), new RegisterSpoolRequest
        {
            FilamentVariantId = seeded.VariantId,
            Label = $"Shelf-{seeded.Tag}",
            SpoolWeightGrams = 1000,
        });
        var spool = await registered.Content.ReadFromJsonAsync<SpoolResponse>();

        (await ListAsync(client, $"search=shelf-{seeded.Tag}")).Select(s => s.Id).Should().Equal(spool!.Id);
    }

    [Fact]
    public async Task Search_TreatsLikeWildcardsAsLiteralText()
    {
        var client = await AuthenticatedClientAsync(_client);
        var seeded = await SeedAsync(client, "Wild", "PLA", "Grey");
        var literal = await client.PostAsJsonAsync(new Uri("/api/v1/spools", UriKind.Relative), new RegisterSpoolRequest
        {
            FilamentVariantId = seeded.VariantId,
            Label = $"zq{seeded.Tag}%x",
            SpoolWeightGrams = 1000,
        });
        var literalSpool = await literal.Content.ReadFromJsonAsync<SpoolResponse>();
        await client.PostAsJsonAsync(new Uri("/api/v1/spools", UriKind.Relative), new RegisterSpoolRequest
        {
            FilamentVariantId = seeded.VariantId,
            Label = $"zq{seeded.Tag}abc",
            SpoolWeightGrams = 1000,
        });

        // An unescaped % would also match the second label.
        var results = await ListAsync(client, $"search=zq{seeded.Tag}%25");
        results.Select(s => s.Id).Should().Equal(literalSpool!.Id);
    }

    [Fact]
    public async Task Filters_CombineWithSearch_AndStateSeparatesStorageFromLoaded()
    {
        var client = await AuthenticatedClientAsync(_client);
        var seeded = await SeedAsync(client, "Combo", "PETG", "Blue");
        var stored = await RegisterSpoolAsync(client, seeded.VariantId);
        var loaded = await RegisterSpoolAsync(client, seeded.VariantId);

        var printerName = $"Printer{seeded.Tag}";
        var printerId = await RegisterSimulatorPrinterAsync(client, printerName);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slot = feedUnits.FeedUnits[0].FeedSlots[0];
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{loaded}/load", UriKind.Relative), new { feedSlotId = slot.Id });

        (await ListAsync(client, $"vendorId={seeded.VendorId}")).Select(s => s.Id).Should().BeEquivalentTo([stored, loaded]);
        (await ListAsync(client, $"filamentId={seeded.FilamentId}&state=InStorage")).Select(s => s.Id).Should().Equal(stored);
        (await ListAsync(client, $"filamentVariantId={seeded.VariantId}&search=combo{seeded.Tag}&state=Using")).Select(s => s.Id).Should().Equal(loaded);

        var using_ = (await ListAsync(client, $"vendorId={seeded.VendorId}&state=Using")).Single();
        using_.FeedSlotLabel.Should().Be($"{printerName} · {slot.DisplayLabel}");
        (await ListAsync(client, $"vendorId={seeded.VendorId}&state=InStorage")).Single().FeedSlotLabel.Should().BeNull();
    }

    [Fact]
    public async Task Search_IsBounded_ExtraTermsBeyondTheCapAreIgnored()
    {
        var client = await AuthenticatedClientAsync(_client);
        var seeded = await SeedAsync(client, "Cap", "PLA", "White");
        var spool = await RegisterSpoolAsync(client, seeded.VariantId);

        // 8 matching terms followed by one that matches nothing: the ninth is beyond the cap.
        var terms = string.Join("%20", Enumerable.Repeat($"cap{seeded.Tag}", 8)) + "%20no-such-term-anywhere";
        (await ListAsync(client, $"search={terms}")).Select(s => s.Id).Should().Equal(spool);
    }

    [Fact]
    public async Task Paging_IsOptIn_ReturnsPagesInCreationOrderWithTheTotalAcrossPages()
    {
        var client = await AuthenticatedClientAsync(_client);
        var seeded = await SeedAsync(client, "Paged", "PLA", "Pink");
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await RegisterSpoolAsync(client, seeded.VariantId));
        }

        var unpaged = await ListPageAsync(client, $"vendorId={seeded.VendorId}");
        unpaged.Spools.Select(s => s.Id).Should().Equal(ids);
        unpaged.TotalCount.Should().Be(5);

        var first = await ListPageAsync(client, $"vendorId={seeded.VendorId}&page=1&pageSize=2");
        first.Spools.Select(s => s.Id).Should().Equal(ids.Take(2));
        first.TotalCount.Should().Be(5);

        var last = await ListPageAsync(client, $"vendorId={seeded.VendorId}&page=3&pageSize=2");
        last.Spools.Select(s => s.Id).Should().Equal(ids.Skip(4));
        last.TotalCount.Should().Be(5);

        var beyond = await ListPageAsync(client, $"vendorId={seeded.VendorId}&page=9&pageSize=2");
        beyond.Spools.Should().BeEmpty();
        beyond.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task Paging_ClampsAnOutOfRangePageSize_AndCountsOnlyMatches()
    {
        var client = await AuthenticatedClientAsync(_client);
        var seeded = await SeedAsync(client, "Clamp", "PLA", "Gold");
        for (var i = 0; i < 3; i++)
        {
            await RegisterSpoolAsync(client, seeded.VariantId);
        }

        var result = await ListPageAsync(client, $"search=clamp{seeded.Tag}&pageSize=0");
        result.Spools.Should().HaveCount(3);
        result.TotalCount.Should().Be(3);

        var onlyPage = await ListPageAsync(client, $"search=clamp{seeded.Tag}&page=1");
        onlyPage.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task State_AcceptsTheNumericValueTheGeneratedClientSends()
    {
        var client = await AuthenticatedClientAsync(_client);
        var seeded = await SeedAsync(client, "Numeric", "PLA", "Lime");
        var spool = await RegisterSpoolAsync(client, seeded.VariantId);

        // SpoolState.InStorage is 0 in the generated web client's numeric enum.
        (await ListAsync(client, $"state=0&vendorId={seeded.VendorId}")).Select(s => s.Id).Should().Equal(spool);
    }

    [Fact]
    public async Task FeedUnits_ReportTheLoadedSpoolsCapacityAlongsideItsRemainingWeight()
    {
        var client = await AuthenticatedClientAsync(_client);
        var seeded = await SeedAsync(client, "Capacity", "PLA", "Coral");
        var spool = await RegisterSpoolAsync(client, seeded.VariantId, spoolWeightGrams: 750);
        var printerId = await RegisterSimulatorPrinterAsync(client);
        var feedUnits = await WaitForFeedUnitsAsync(client, printerId);
        var slotId = feedUnits.FeedUnits[0].FeedSlots[0].Id;
        await client.PutAsJsonAsync(new Uri($"/api/v1/spools/{spool}/load", UriKind.Relative), new { feedSlotId = slotId });

        var refreshed = await WaitForFeedUnitsAsync(client, printerId, r => r.FeedUnits[0].FeedSlots[0].CurrentSpool is not null);

        refreshed.FeedUnits[0].FeedSlots[0].CurrentSpool!.SpoolWeightGrams.Should().Be(750);
    }
}
