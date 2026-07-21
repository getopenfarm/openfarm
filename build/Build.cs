using System;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using OpenFarm.Tools.ProvisionSlicers;

/// <summary>
/// The single build/test/provisioning entry point — the same targets run identically on a
/// developer's machine and in CI (`./build.sh &lt;Target&gt;` / `./build.cmd &lt;Target&gt;`), replacing
/// the scattered ad hoc shell steps in `.github/workflows/ci.yml` and the standalone
/// `tools/provision-slicers` console app (see <see cref="ProvisionSlicers"/> below, which invokes
/// that project's pipeline directly as a library instead of via its own CLI).
/// </summary>
class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Test);

    [Parameter("dotnet build/test configuration. Default is Release.")]
    readonly string Configuration = "Release";

    AbsolutePath BackendSolution => RootDirectory / "api" / "OpenFarm.slnx";

    AbsolutePath WebDirectory => RootDirectory / "web";

    Target Restore => _ => _
        .Executes(() =>
        {
            ProcessTasks.StartProcess("dotnet", $"restore {BackendSolution}").AssertZeroExitCode();
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            ProcessTasks.StartProcess("dotnet", $"build {BackendSolution} --configuration {Configuration} --no-restore").AssertZeroExitCode();
        });

    Target FormatCheck => _ => _
        .Executes(() =>
        {
            ProcessTasks.StartProcess("dotnet", $"format {BackendSolution} --verify-no-changes").AssertZeroExitCode();
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            ProcessTasks.StartProcess("dotnet", $"test {BackendSolution} --no-build --configuration {Configuration}").AssertZeroExitCode();
        });

    Target FrontendInstall => _ => _
        .Executes(() =>
        {
            ProcessTasks.StartProcess("bun", "install --frozen-lockfile", workingDirectory: WebDirectory).AssertZeroExitCode();
        });

    Target FrontendTypecheck => _ => _
        .DependsOn(FrontendInstall)
        .Executes(() =>
        {
            ProcessTasks.StartProcess("bunx", "tsc -b", workingDirectory: WebDirectory).AssertZeroExitCode();
        });

    Target FrontendLint => _ => _
        .DependsOn(FrontendInstall)
        .Executes(() =>
        {
            ProcessTasks.StartProcess("bun", "run lint", workingDirectory: WebDirectory).AssertZeroExitCode();
        });

    Target FrontendTest => _ => _
        .DependsOn(FrontendInstall)
        .Executes(() =>
        {
            ProcessTasks.StartProcess("bun", "run test", workingDirectory: WebDirectory).AssertZeroExitCode();
        });

    Target FrontendBuild => _ => _
        .DependsOn(FrontendInstall)
        .Executes(() =>
        {
            ProcessTasks.StartProcess("bun", "run build", workingDirectory: WebDirectory).AssertZeroExitCode();
        });

    Target FrontendContractDriftCheck => _ => _
        .DependsOn(FrontendInstall)
        .Executes(() =>
        {
            ProcessTasks.StartProcess("bunx", "orval", workingDirectory: WebDirectory).AssertZeroExitCode();
            ProcessTasks.StartProcess("git", "diff --exit-code src/lib/api/generated", workingDirectory: WebDirectory).AssertZeroExitCode();
        });

    // --- Slicer provisioning (dev/build-time only) ---
    //
    // The running application never downloads slicer binaries at runtime (FR-018) — obtaining them
    // is exclusively this dev/build-time activity. This Target reuses tools/provision-slicers'
    // pipeline (ManifestParser, Provisioner, Lockfile, HttpAssetDownloader, DefaultAssetExtractor)
    // directly as a library; there is no longer a separate CLI entry point for it.

    [Parameter("Path to the slicer version manifest JSON. Default: <repo root>/slicers.manifest.json.")]
    readonly AbsolutePath Manifest = null;

    [Parameter("Output directory for provisioned slicer bundles. Default: <repo root>/artifacts/slicers.")]
    readonly AbsolutePath Out = null;

    [Parameter("Target platform to provision for: Linux, Windows, or MacOs. Default: Linux.")]
    readonly string Platform = "Linux";

    AbsolutePath EffectiveManifest => Manifest ?? RootDirectory / "slicers.manifest.json";

    AbsolutePath EffectiveOut => Out ?? RootDirectory / "artifacts" / "slicers";

    Target ProvisionSlicers => _ => _
        .Executes(async () =>
        {
            if (!Enum.TryParse<OpenFarm.Tools.ProvisionSlicers.Platform>(Platform, ignoreCase: true, out var platform))
            {
                throw new ArgumentException($"Unknown platform '{Platform}'. Expected Linux, Windows, or MacOs.");
            }

            var manifestJson = await System.IO.File.ReadAllTextAsync(EffectiveManifest);
            var parseResult = ManifestParser.Parse(manifestJson);
            if (!parseResult.Success)
            {
                foreach (var error in parseResult.Errors)
                {
                    Console.Error.WriteLine($"  - {error}");
                }

                throw new Exception($"Invalid manifest '{EffectiveManifest}'.");
            }

            System.IO.Directory.CreateDirectory(EffectiveOut);
            var lockfilePath = EffectiveManifest.Parent / "slicers.lock.json";
            var lockfile = Lockfile.Load(lockfilePath);

            using var downloader = new HttpAssetDownloader();
            var provisioner = new Provisioner(downloader, new DefaultAssetExtractor());

            var entriesForPlatform = parseResult.Manifest!.Slicers
                .Where(entry => entry.Platforms.Contains(platform))
                .ToList();

            var anyFailed = false;
            foreach (var entry in entriesForPlatform)
            {
                var request = new ProvisionEntryRequest(entry.Engine, entry.Version, platform);
                var result = await provisioner.ProvisionAsync(request, EffectiveOut, lockfile, default);

                Console.WriteLine($"[{result.Outcome}] {result.Engine} {result.Version} ({result.Platform}): {result.Message}");
                if (result.Outcome == ProvisionOutcome.Failed)
                {
                    anyFailed = true;
                }
            }

            lockfile.Save(lockfilePath);

            if (entriesForPlatform.Count == 0)
            {
                Console.WriteLine($"No manifest entries declare platform {platform} — nothing to provision.");
            }

            if (anyFailed)
            {
                throw new Exception("One or more slicer entries failed to provision.");
            }
        });
}
