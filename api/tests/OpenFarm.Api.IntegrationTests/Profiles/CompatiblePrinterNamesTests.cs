using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Api.Endpoints.Auth;
using OpenFarm.Api.Endpoints.Profiles;
using OpenFarm.Application.Auth;

namespace OpenFarm.Api.IntegrationTests.Profiles;

// FR-016, US3 Acceptance Scenario 3 — a filament profile's declared compatible-printer names are
// shown as plain declared text, identical whether or not the named printer exists elsewhere in the
// catalog (no resolution against MachineProfile is attempted, research.md §11).
public class CompatiblePrinterNamesTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var loginResponse = await _client.PostAsJsonAsync(
            new Uri("/api/v1/auth/login", UriKind.Relative),
            new LoginCommand("admin", "admin-openfarm"));
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return _client;
    }

    private static async Task ConfirmSingleFilamentAsync(HttpClient client, string fileEntryName, string rawJson)
    {
        var bundle = ProfileBundleTestHelper.BuildZip(($"filament/{fileEntryName}.json", rawJson));
        var selections = JsonSerializer.Serialize(new object[]
        {
            new { type = "Filament", name = fileEntryName, duplicateAction = (string?)null },
        });
        using var content = ProfileBundleTestHelper.BuildUploadContent(bundle, "bundle.zip", selections);
        var response = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), content);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task FilamentProfile_CompatiblePrinterNamesShown_RegardlessOfWhetherPrinterExistsInCatalog()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");

        // Filament A declares a printer that IS also in the catalog (a same-bundle-style printer).
        var printerName = $"Declared Printer {suffix}";
        var filamentWithExistingPrinterName = $"PLA With Existing Printer {suffix}";
        await ConfirmSingleFilamentAsync(client, printerName,
            $$"""{"name":"{{printerName}}"}""");
        var machineBundle = ProfileBundleTestHelper.BuildZip(($"machine/{printerName}.json", $$"""{"name":"{{printerName}}"}"""));
        var machineSelections = JsonSerializer.Serialize(new object[] { new { type = "Machine", name = printerName, duplicateAction = (string?)null } });
        using (var machineContent = ProfileBundleTestHelper.BuildUploadContent(machineBundle, "bundle.zip", machineSelections))
        {
            var machineResponse = await client.PostAsync(new Uri("/api/v1/profiles/imports/confirm", UriKind.Relative), machineContent);
            machineResponse.EnsureSuccessStatusCode();
        }
        await ConfirmSingleFilamentAsync(client, filamentWithExistingPrinterName,
            $$"""{"name":"{{filamentWithExistingPrinterName}}","compatible_printers":["{{printerName}}"]}""");

        // Filament B declares a printer that does NOT exist anywhere in the catalog.
        var unknownPrinterName = $"Unimported Printer {suffix}";
        var filamentWithUnknownPrinterName = $"PLA With Unknown Printer {suffix}";
        await ConfirmSingleFilamentAsync(client, filamentWithUnknownPrinterName,
            $$"""{"name":"{{filamentWithUnknownPrinterName}}","compatible_printers":["{{unknownPrinterName}}"]}""");

        var filamentList = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(new Uri("/api/v1/profiles/filament", UriKind.Relative));

        var withExisting = filamentList!.Items.Should().ContainSingle(p => p.Name == filamentWithExistingPrinterName).Subject;
        withExisting.CompatiblePrinterNames.Should().ContainSingle().Which.Should().Be(printerName);

        var withUnknown = filamentList.Items.Should().ContainSingle(p => p.Name == filamentWithUnknownPrinterName).Subject;
        withUnknown.CompatiblePrinterNames.Should().ContainSingle().Which.Should().Be(unknownPrinterName);
    }

    [Fact]
    public async Task FilamentProfile_NameSuffixFallback_UsedWhenNoBaseAndNoDeclaredList()
    {
        var client = await AuthenticatedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var filamentName = $"Bambu PLA Basic @ Bambu X1 Carbon {suffix}";

        await ConfirmSingleFilamentAsync(client, filamentName, $$"""{"name":"{{filamentName}}"}""");

        var filamentList = await client.GetFromJsonAsync<ListFilamentProfilesResponse>(new Uri("/api/v1/profiles/filament", UriKind.Relative));
        var entry = filamentList!.Items.Should().ContainSingle(p => p.Name == filamentName).Subject;

        entry.CompatiblePrinterNames.Should().ContainSingle().Which.Should().Be($"Bambu X1 Carbon {suffix}");
    }
}
