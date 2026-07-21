using System.Diagnostics;
using System.IO.Compression;

namespace OpenFarm.Tools.ProvisionSlicers;

/// <summary>Places a downloaded asset's contents into a bundle leaf directory, per its archive form.</summary>
public interface IAssetExtractor
{
    Task ExtractAsync(byte[] assetBytes, string assetFileName, string destinationLeafDirectory, CancellationToken cancellationToken);
}

/// <summary>
/// Real implementation — a <c>.zip</c> (e.g. OrcaSlicer/Bambu Studio/SuperSlicer's Windows portable
/// build) is extracted directly (built-in, no external dependency; <see cref="SlicerExecutableResolver"/>-
/// equivalent lookup on the app side searches the leaf recursively, so a single wrapping folder inside
/// the zip needs no special flattening here). An <c>.AppImage</c> is extracted via its own
/// <c>--appimage-extract</c> flag (matching the scanner's expectation of an <c>AppRun</c> entry point
/// inside the leaf, since AppImages need FUSE to run directly and containers typically lack it — the
/// same reasoning infra/docker/api.Dockerfile already applied). A <c>.dmg</c> is mounted and its
/// <c>.app</c> bundle copied out via <c>hdiutil</c> — real extraction, but only possible when this tool
/// itself runs on macOS (Apple's disk-image format has no cross-platform .NET reader); on any other
/// host the raw <c>.dmg</c> is written to the leaf and extraction is reported as failed with an
/// actionable message; a maintainer completes it on a Mac. Manual-only, never a CI gate (research.md
/// §8/§9) — same carve-out as the real Bambu LAN connector / physical printer hardware.
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

        if (assetFileName.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractDmgAsync(assetBytes, destinationLeafDirectory, cancellationToken);
            return;
        }

        // Anything else unrecognized: no known extraction — place the raw asset in the leaf so at
        // least the download/integrity-verified bytes aren't lost, and let a maintainer complete it.
        await File.WriteAllBytesAsync(Path.Combine(destinationLeafDirectory, assetFileName), assetBytes, cancellationToken);
    }

    private static async Task ExtractDmgAsync(byte[] assetBytes, string destinationLeafDirectory, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            await File.WriteAllBytesAsync(Path.Combine(destinationLeafDirectory, "unextracted.dmg"), assetBytes, cancellationToken);
            throw new InvalidOperationException(
                "Extracting a macOS .dmg requires running this tool on macOS (hdiutil) — Apple's disk-image " +
                $"format has no cross-platform .NET reader. The raw .dmg was written to '{destinationLeafDirectory}'; " +
                "mount it, copy the .app bundle into that same directory, and re-run to pick it up as already present.");
        }

        var workDirectory = Directory.CreateTempSubdirectory("openfarm-provision-dmg-");
        var mountPoint = Path.Combine(workDirectory.FullName, "mount");
        try
        {
            var dmgPath = Path.Combine(workDirectory.FullName, "asset.dmg");
            await File.WriteAllBytesAsync(dmgPath, assetBytes, cancellationToken);
            Directory.CreateDirectory(mountPoint);

            await RunProcessAsync(
                "hdiutil", ["attach", dmgPath, "-nobrowse", "-readonly", "-mountpoint", mountPoint], cancellationToken);

            try
            {
                var appBundle = Directory.EnumerateDirectories(mountPoint, "*.app", SearchOption.TopDirectoryOnly).FirstOrDefault()
                    ?? throw new InvalidOperationException("No .app bundle found inside the mounted disk image.");

                CopyDirectory(appBundle, Path.Combine(destinationLeafDirectory, Path.GetFileName(appBundle)));
            }
            finally
            {
                await RunProcessAsync("hdiutil", ["detach", mountPoint, "-quiet"], cancellationToken, throwOnNonZeroExit: false);
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

    private static async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken, bool throwOnNonZeroExit = true)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        await process.WaitForExitAsync(cancellationToken);
        if (throwOnNonZeroExit && process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"'{fileName}' exited with code {process.ExitCode}: {stderr}");
        }
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
