using OpenFarm.Tools.ProvisionSlicers;

var options = CliOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("Usage: provision-slicers --manifest <path> --out <dir> --platform <Linux|Windows|MacOs>");
    return 1;
}

var manifestJson = await File.ReadAllTextAsync(options.ManifestPath);
var parseResult = ManifestParser.Parse(manifestJson);
if (!parseResult.Success)
{
    Console.Error.WriteLine($"Invalid manifest '{options.ManifestPath}':");
    foreach (var error in parseResult.Errors)
    {
        Console.Error.WriteLine($"  - {error}");
    }

    return 1;
}

Directory.CreateDirectory(options.OutputDirectory);
var lockfilePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.ManifestPath)) ?? ".", "slicers.lock.json");
var lockfile = Lockfile.Load(lockfilePath);

using var downloader = new HttpAssetDownloader();
var provisioner = new Provisioner(downloader, new DefaultAssetExtractor());

var entriesForPlatform = parseResult.Manifest!.Slicers
    .Where(entry => entry.Platforms.Contains(options.Platform))
    .ToList();

var anyFailed = false;
foreach (var entry in entriesForPlatform)
{
    var request = new ProvisionEntryRequest(entry.Engine, entry.Version, options.Platform);
    var result = await provisioner.ProvisionAsync(request, options.OutputDirectory, lockfile, CancellationToken.None);

    Console.WriteLine($"[{result.Outcome}] {result.Engine} {result.Version} ({result.Platform}): {result.Message}");
    if (result.Outcome == ProvisionOutcome.Failed)
    {
        anyFailed = true;
    }
}

lockfile.Save(lockfilePath);

if (entriesForPlatform.Count == 0)
{
    Console.WriteLine($"No manifest entries declare platform {options.Platform} — nothing to provision.");
}

return anyFailed ? 1 : 0;

internal sealed record CliOptions(string ManifestPath, string OutputDirectory, Platform Platform)
{
    public static CliOptions? Parse(string[] args)
    {
        string? manifestPath = null;
        string? outputDirectory = null;
        Platform? platform = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--manifest" when i + 1 < args.Length:
                    manifestPath = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    outputDirectory = args[++i];
                    break;
                case "--platform" when i + 1 < args.Length:
                    if (Enum.TryParse<Platform>(args[++i], out var parsedPlatform))
                    {
                        platform = parsedPlatform;
                    }

                    break;
            }
        }

        return manifestPath is not null && outputDirectory is not null && platform is not null
            ? new CliOptions(manifestPath, outputDirectory, platform.Value)
            : null;
    }
}
