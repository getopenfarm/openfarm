using System.IO.Compression;
using System.Net.Http.Headers;

namespace OpenFarm.Api.IntegrationTests.Profiles;

// Builds small synthetic in-memory zip bundles for exercising the preview/confirm endpoints —
// real-format fidelity is already covered by OpenFarm.Infrastructure.Tests' ProfileBundleParserTests
// against real vendor fixtures; these tests exercise the API/persistence layer given any valid
// bundle shape.
internal static class ProfileBundleTestHelper
{
    public static byte[] BuildZip(params (string Path, string Json)[] entries)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, json) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write(json);
            }
        }

        return memoryStream.ToArray();
    }

    public static MultipartFormDataContent BuildUploadContent(byte[] zipBytes, string fileName, string? selectionsJson = null)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(zipBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "File", fileName);

        if (selectionsJson is not null)
        {
            content.Add(new StringContent(selectionsJson), "Selections");
        }

        return content;
    }

    public static byte[] SampleBundle(string suffix) => BuildZip(
        ($"machine/Test Printer {suffix}.json", $$"""{"name":"Test Printer {{suffix}}","nozzle_diameter":["0.4"]}"""),
        ($"filament/Test PLA {suffix}.json", $$"""{"name":"Test PLA {{suffix}}","filament_type":["PLA"]}"""),
        ($"filament/Test PETG {suffix}.json", $$"""{"name":"Test PETG {{suffix}}","filament_type":["PETG"]}"""));
}
