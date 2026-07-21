using System.Diagnostics;
using System.IO.Compression;

namespace OpenFarm.Tools.ProvisionSlicers;

/// <summary>Places a downloaded asset's contents into a bundle leaf directory, per its archive form.</summary>
public interface IAssetExtractor
{
    Task ExtractAsync(byte[] assetBytes, string assetFileName, string destinationLeafDirectory, CancellationToken cancellationToken);
}

/// <summary>
/// Real implementation — a <c>.zip</c> is extracted directly (built-in, no external dependency); an
/// <c>.AppImage</c> is extracted via its own <c>--appimage-extract</c> flag (matching
/// <c>BundledSlicerScanner</c>'s expectation of an <c>AppRun</c> entry point inside the leaf, since
/// AppImages need FUSE to run directly and containers typically lack it — the same reasoning
/// infra/docker/api.Dockerfile already applied). A <c>.dmg</c> has no cross-platform .NET extraction
/// API — it's written to the leaf as-is and left for manual completion on a macOS host. Manual-only,
/// never a CI gate (research.md §8/§9).
/// </summary>
public sealed class DefaultAssetExtractor : IAssetExtractor
{
    public async Task ExtractAsync(byte[] assetBytes, string assetFileName, string destinationLeafDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationLeafDirectory);

        if (assetFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var tempZipPath = Path.GetTempFileName();
            try
            {
                await File.WriteAllBytesAsync(tempZipPath, assetBytes, cancellationToken);
                ZipFile.ExtractToDirectory(tempZipPath, destinationLeafDirectory, overwriteFiles: true);
            }
            finally
            {
                File.Delete(tempZipPath);
            }

            return;
        }

        if (assetFileName.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractAppImageAsync(assetBytes, destinationLeafDirectory, cancellationToken);
            return;
        }

        // .dmg (and anything else unrecognized): no portable extraction available — place the raw
        // asset in the leaf and let a maintainer complete extraction manually on the target platform.
        await File.WriteAllBytesAsync(Path.Combine(destinationLeafDirectory, assetFileName), assetBytes, cancellationToken);
    }

    private static async Task ExtractAppImageAsync(byte[] assetBytes, string destinationLeafDirectory, CancellationToken cancellationToken)
    {
        var workDirectory = Directory.CreateTempSubdirectory("openfarm-provision-appimage-");
        try
        {
            var appImagePath = Path.Combine(workDirectory.FullName, "app.AppImage");
            await File.WriteAllBytesAsync(appImagePath, assetBytes, cancellationToken);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(appImagePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            var startInfo = new ProcessStartInfo(appImagePath, "--appimage-extract")
            {
                WorkingDirectory = workDirectory.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the AppImage extraction process.");
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException($"AppImage extraction exited with code {process.ExitCode}: {stderr}");
            }

            var extractedRoot = Path.Combine(workDirectory.FullName, "squashfs-root");
            foreach (var entry in Directory.EnumerateFileSystemEntries(extractedRoot))
            {
                var destination = Path.Combine(destinationLeafDirectory, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                {
                    CopyDirectory(entry, destination);
                }
                else
                {
                    File.Copy(entry, destination, overwrite: true);
                }
            }
        }
        finally
        {
            try
            {
                workDirectory.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDir))
        {
            CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
        }
    }
}
