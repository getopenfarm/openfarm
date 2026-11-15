using System.IO.Compression;

namespace OpenFarm.Api.IntegrationTests.Parts;

/// <summary>
/// A minimal project 3MF: what the library accepts, since only a file that carries its own machine, process and plate
/// can be sliced. <paramref name="settings"/> is its <c>Metadata/project_settings.config</c>.
/// </summary>
internal static class ProjectThreeMf
{
    public static byte[] Bytes(string settings = "{}")
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var model = new StreamWriter(zip.CreateEntry("3D/3dmodel.model").Open()))
                model.Write("<model><resources/><build/></model>");
            using var config = new StreamWriter(zip.CreateEntry("Metadata/project_settings.config").Open());
            config.Write(settings);
        }

        return output.ToArray();
    }
}
