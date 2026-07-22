# OrcaSlicer/Bambu Studio CLI Invocation Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix `PrusaSliceFamilyInvocation` so slicing an STL/3MF with a bundled OrcaSlicer or Bambu Studio
actually works, instead of failing with `The OrcaSlicer CLI exited with code 254: Invalid option
--export-gcode setup params error`.

**Architecture:** `PrusaSliceFamilyInvocation` currently builds
`--slice 0 --load-filaments "<material>" --nozzle-diameter <n> --export-gcode "<out>" "<model>"` —
lifted unchanged from the old single-engine `CliSlicerEngine`, on the assumption OrcaSlicer/Bambu Studio
share PrusaSlicer's classic flag-per-value CLI. They don't: neither engine has `--nozzle-diameter` or
`--export-gcode` at all (confirmed against the OrcaSlicer/Bambu Studio CLI flag reference at
printago.io/slicer-cli — 54/55 flags each, neither flag present). Nozzle diameter and filament type are
only settable via JSON files loaded through `--load-settings`/`--load-filaments`; the only export flag is
`--export-3mf`, which writes a *gcode-bearing 3MF* (a zip archive with the real G-code at
`Metadata/plate_1.gcode` inside it — confirmed via GitHub issues/Bambu forum threads on the
`.gcode.3mf` format), not a plain `.gcode` file. `CliSlicerEngine` currently does
`File.ReadAllTextAsync(outputPath)`, which would choke on that zip even once the flags are fixed.

The fix: (1) `PrusaSliceFamilyInvocation` writes two small settings JSON files into the per-slice temp
work directory and builds `--load-settings`/`--load-filaments`/`--export-3mf` args instead; (2)
`ISlicerInvocation`'s output contract grows two fields (`OutputPath`, `ProducesGCodeBearingThreeMf`) so
`CliSlicerEngine` knows where to look and how to read what it finds; (3) `CliSlicerEngine` unzips and
reads `Metadata/plate_1.gcode` when the strategy says so, otherwise reads the file as plain text exactly
as before. `SuperSlicerInvocation` is untouched — SuperSlicer is a real fork of classic PrusaSlicer and
does still have `--export-gcode`/`--nozzle-diameter`/`--filament-type`; nothing here suggests it's broken.

**Tech Stack:** .NET 10 / C# 13, xUnit + FluentAssertions, `System.Text.Json`, `System.IO.Compression`
(both already used elsewhere in this codebase — no new package references).

## Global Constraints

- `dotnet format api/OpenFarm.slnx --verify-no-changes` and `dotnet test api/OpenFarm.slnx` must both pass
  before this is done (CLAUDE.md).
- Nullable reference types on, warnings-as-errors in Release builds (CLAUDE.md) — build in Release at the
  end to catch anything Debug wouldn't.
- This CLI-invocation layer's exact flag surface is explicitly a **manual-validation carve-out** in this
  repo (see `CliSlicerEngine.cs`'s and `SuperSlicerInvocation.cs`'s own doc comments, and
  `specs/008-orca-slicer-multi-version/quickstart.md`'s "Real-CLI validation is manual ... never a CI
  gate"). This plan's automated tests cover argument-building and 3MF-unzip logic deterministically; they
  cannot and do not prove the real OrcaSlicer binary accepts the generated JSON. Task 3 calls this out as a
  manual follow-up — do not skip reporting it.
- One `ISlicerInvocation` implementation per engine family (existing pattern) — don't fold SuperSlicer's
  logic into the same class or add a branch inside `CliSlicerEngine` for engine-specific behavior.

---

## File Map

- Modify: `api/src/OpenFarm.Application/Parts/ISlicerInvocation.cs` — `SlicerProcessInvocation` gains
  `OutputPath`/`ProducesGCodeBearingThreeMf`; `BuildInvocation`'s last parameter becomes a work-directory
  path instead of a precomputed output file path.
- Modify: `api/src/OpenFarm.Infrastructure/Parts/SlicerInvocation/PrusaSliceFamilyInvocation.cs` — real
  fix: generate machine/filament settings JSON, build `--export-3mf` args.
- Modify: `api/src/OpenFarm.Infrastructure/Parts/SlicerInvocation/SuperSlicerInvocation.cs` — mechanical
  update to the new record shape only; its argument-building logic is unchanged.
- Modify: `api/src/OpenFarm.Infrastructure/Parts/CliSlicerEngine.cs` — call `BuildInvocation` with the
  work directory instead of a precomputed output path; branch G-code reading on
  `ProducesGCodeBearingThreeMf`; add an `internal static` helper to unzip `Metadata/plate_1.gcode`.
- Modify: `api/tests/OpenFarm.Infrastructure.Tests/Parts/SlicerInvocationTests.cs` — rewrite both
  `BuildInvocation` tests for the new contract.
- Modify: `api/tests/OpenFarm.Infrastructure.Tests/Parts/CliSlicerEngineTests.cs` — add coverage for the
  new 3MF-unzip helper (via `InternalsVisibleTo`, same pattern as `BambuCertificateTrust`).

---

### Task 1: Fix the `ISlicerInvocation` contract and both strategies

**Files:**
- Modify: `api/src/OpenFarm.Application/Parts/ISlicerInvocation.cs`
- Modify: `api/src/OpenFarm.Infrastructure/Parts/SlicerInvocation/PrusaSliceFamilyInvocation.cs`
- Modify: `api/src/OpenFarm.Infrastructure/Parts/SlicerInvocation/SuperSlicerInvocation.cs`
- Test: `api/tests/OpenFarm.Infrastructure.Tests/Parts/SlicerInvocationTests.cs`

**Interfaces:**
- Produces: `SlicerProcessInvocation(string FileName, string Arguments, string OutputPath, bool ProducesGCodeBearingThreeMf)`.
  `ISlicerInvocation.BuildInvocation(AvailableSlicer slicer, string modelFilePath, string materialType, decimal nozzleDiameterMm, string workDirectoryPath)` —
  note the last parameter is now a **directory**, not a file path. Task 2's `CliSlicerEngine` consumes
  `processInvocation.OutputPath` and `processInvocation.ProducesGCodeBearingThreeMf`.

This task changes the interface and both implementers together — an interface-only or
one-implementer-only commit wouldn't compile, so there's no meaningful intermediate review point.

- [ ] **Step 1: Rewrite the test file to assert the new contract (it will fail to compile until Steps 2-4 land)**

Replace the full contents of `api/tests/OpenFarm.Infrastructure.Tests/Parts/SlicerInvocationTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Parts.SlicerInvocation;

namespace OpenFarm.Infrastructure.Tests.Parts;

public class SlicerInvocationTests
{
    private static readonly AvailableSlicer Orca = new(SlicerEngine.OrcaSlicer, "2.2.0", SlicerPlatform.Linux, "/bundle/orcaslicer/2.2.0/linux/AppRun");
    private static readonly AvailableSlicer SuperSlicerBinary = new(SlicerEngine.SuperSlicer, "2.5.59.13", SlicerPlatform.Linux, "/bundle/superslicer/2.5.59.13/linux/AppRun");

    [Fact]
    public void PrusaSliceFamilyInvocation_SupportsOrcaSlicerAndBambuStudioOnly()
    {
        var invocation = new PrusaSliceFamilyInvocation();

        invocation.SupportsEngine(SlicerEngine.OrcaSlicer).Should().BeTrue();
        invocation.SupportsEngine(SlicerEngine.BambuStudio).Should().BeTrue();
        invocation.SupportsEngine(SlicerEngine.SuperSlicer).Should().BeFalse();
    }

    [Fact]
    public void PrusaSliceFamilyInvocation_BuildInvocation_ExportsAGCodeBearingThreeMfViaGeneratedSettingsJson()
    {
        var workDirectory = Directory.CreateTempSubdirectory("slicer-invocation-test-");
        try
        {
            var invocation = new PrusaSliceFamilyInvocation();

            var result = invocation.BuildInvocation(Orca, "/tmp/model.3mf", "PETG", 0.4m, workDirectory.FullName);

            result.FileName.Should().Be(Orca.ExecutablePath);
            result.ProducesGCodeBearingThreeMf.Should().BeTrue();
            result.OutputPath.Should().Be(Path.Combine(workDirectory.FullName, "output.gcode.3mf"));

            var machineSettingsPath = Path.Combine(workDirectory.FullName, "machine_settings.json");
            var filamentSettingsPath = Path.Combine(workDirectory.FullName, "filament_settings.json");

            result.Arguments.Should().Be(
                $"--slice 0 --load-settings \"{machineSettingsPath}\" --load-filaments \"{filamentSettingsPath}\" " +
                $"--export-3mf \"{result.OutputPath}\" \"/tmp/model.3mf\"");

            File.Exists(machineSettingsPath).Should().BeTrue();
            using (var doc = JsonDocument.Parse(File.ReadAllText(machineSettingsPath)))
            {
                doc.RootElement.GetProperty("nozzle_diameter")[0].GetString().Should().Be("0.4");
            }

            File.Exists(filamentSettingsPath).Should().BeTrue();
            using (var doc = JsonDocument.Parse(File.ReadAllText(filamentSettingsPath)))
            {
                doc.RootElement.GetProperty("filament_type")[0].GetString().Should().Be("PETG");
            }
        }
        finally
        {
            workDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void SuperSlicerInvocation_SupportsSuperSlicerOnly()
    {
        var invocation = new SuperSlicerInvocation();

        invocation.SupportsEngine(SlicerEngine.SuperSlicer).Should().BeTrue();
        invocation.SupportsEngine(SlicerEngine.OrcaSlicer).Should().BeFalse();
        invocation.SupportsEngine(SlicerEngine.BambuStudio).Should().BeFalse();
    }

    [Fact]
    public void SuperSlicerInvocation_BuildInvocation_ProducesItsDivergentArgumentList()
    {
        var invocation = new SuperSlicerInvocation();

        var result = invocation.BuildInvocation(SuperSlicerBinary, "/tmp/model.3mf", "Generic PLA", 0.4m, "/tmp");

        result.FileName.Should().Be(SuperSlicerBinary.ExecutablePath);
        result.ProducesGCodeBearingThreeMf.Should().BeFalse();
        result.OutputPath.Should().Be(Path.Combine("/tmp", "output.gcode"));
        result.Arguments.Should().Be(
            "--slice --filament-type \"Generic PLA\" --nozzle-diameter 0.4 --export-gcode \"/tmp/output.gcode\" \"/tmp/model.3mf\"");
    }
}
```

- [ ] **Step 2: Run the test file to confirm it fails to compile**

Run: `dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~SlicerInvocationTests"`
Expected: build error — `SlicerProcessInvocation` doesn't have an `OutputPath`/`ProducesGCodeBearingThreeMf`
member yet, and `BuildInvocation` signatures don't match.

- [ ] **Step 3: Update the interface**

Replace the full contents of `api/src/OpenFarm.Application/Parts/ISlicerInvocation.cs`:

```csharp
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

/// <summary>
/// The process <see cref="FileName"/> + argument list a resolved <see cref="ISlicerInvocation"/>
/// strategy builds for one slice (research.md §4), plus where it wrote its result and whether that
/// result is a raw <c>.gcode</c> file or a gcode-bearing <c>.3mf</c> archive the caller must unzip
/// (OrcaSlicer/Bambu Studio have no direct G-code export flag — see
/// <c>PrusaSliceFamilyInvocation</c>).
/// </summary>
public sealed record SlicerProcessInvocation(string FileName, string Arguments, string OutputPath, bool ProducesGCodeBearingThreeMf);

/// <summary>
/// Per-engine CLI-invocation strategy — isolates each engine's own command-line form (research.md
/// §4) so <c>CliSlicerEngine</c> stays a pure orchestration shell (temp dir, process lifetime,
/// timeout, failure mapping) regardless of which engine it's driving. Adding a fourth engine (FR-022)
/// means one more implementation, not a change to any existing one.
/// </summary>
public interface ISlicerInvocation
{
    /// <summary>Whether this strategy knows how to invoke <paramref name="engine"/>.</summary>
    bool SupportsEngine(SlicerEngine engine);

    /// <summary>
    /// Builds the process invocation for slicing <paramref name="modelFilePath"/> against
    /// <paramref name="slicer"/>. <paramref name="workDirectoryPath"/> is the per-slice temp
    /// directory <c>CliSlicerEngine</c> creates and deletes — a strategy that needs to write its own
    /// settings files (rather than pass raw values as flags) writes them there, and reports its own
    /// chosen output file name back via <see cref="SlicerProcessInvocation.OutputPath"/>.
    /// </summary>
    SlicerProcessInvocation BuildInvocation(
        AvailableSlicer slicer,
        string modelFilePath,
        string materialType,
        decimal nozzleDiameterMm,
        string workDirectoryPath);
}
```

- [ ] **Step 4: Rewrite `PrusaSliceFamilyInvocation` — the actual bug fix**

Replace the full contents of
`api/src/OpenFarm.Infrastructure/Parts/SlicerInvocation/PrusaSliceFamilyInvocation.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts.SlicerInvocation;

/// <summary>
/// OrcaSlicer/Bambu Studio's real CLI form (research.md §4, revised). Unlike PrusaSlicer/SuperSlicer,
/// neither engine has a <c>--nozzle-diameter</c> or <c>--export-gcode</c> flag: nozzle diameter and
/// filament type are only settable via JSON files loaded through <c>--load-settings</c>/
/// <c>--load-filaments</c>, and the only export flag is <c>--export-3mf</c>, which writes a
/// gcode-bearing 3MF (the real G-code lives inside it at <c>Metadata/plate_1.gcode</c>, unzipped by
/// <c>CliSlicerEngine</c> when <see cref="SlicerProcessInvocation.ProducesGCodeBearingThreeMf"/> is
/// true). The previous "--slice 0 --load-filaments … --nozzle-diameter … --export-gcode …" form
/// this class used to build doesn't parse on either engine's real binary (exit 254, "Invalid option
/// --export-gcode").
/// </summary>
public sealed class PrusaSliceFamilyInvocation : ISlicerInvocation
{
    public bool SupportsEngine(SlicerEngine engine) => engine is SlicerEngine.OrcaSlicer or SlicerEngine.BambuStudio;

    public SlicerProcessInvocation BuildInvocation(
        AvailableSlicer slicer, string modelFilePath, string materialType, decimal nozzleDiameterMm, string workDirectoryPath)
    {
        var machineSettingsPath = Path.Combine(workDirectoryPath, "machine_settings.json");
        File.WriteAllText(machineSettingsPath, JsonSerializer.Serialize(new
        {
            nozzle_diameter = new[] { nozzleDiameterMm.ToString(CultureInfo.InvariantCulture) },
        }));

        var filamentSettingsPath = Path.Combine(workDirectoryPath, "filament_settings.json");
        File.WriteAllText(filamentSettingsPath, JsonSerializer.Serialize(new
        {
            filament_type = new[] { materialType },
        }));

        var outputPath = Path.Combine(workDirectoryPath, "output.gcode.3mf");

        var arguments = $"--slice 0 --load-settings \"{machineSettingsPath}\" --load-filaments \"{filamentSettingsPath}\" " +
            $"--export-3mf \"{outputPath}\" \"{modelFilePath}\"";

        return new SlicerProcessInvocation(slicer.ExecutablePath, arguments, outputPath, ProducesGCodeBearingThreeMf: true);
    }
}
```

- [ ] **Step 5: Update `SuperSlicerInvocation` for the new record shape (logic unchanged)**

Replace the full contents of
`api/src/OpenFarm.Infrastructure/Parts/SlicerInvocation/SuperSlicerInvocation.cs`:

```csharp
using System.Globalization;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts.SlicerInvocation;

/// <summary>
/// SuperSlicer's PrusaSlicer-fork CLI form (research.md §4) — its slice/export flags diverge from
/// the shared <see cref="PrusaSliceFamilyInvocation"/> form (a filament-type flag instead of a named
/// filament-profile load, and a plain <c>--slice</c> switch with no plate index). The executable
/// itself was already resolved per-platform (AppImage <c>AppRun</c> / Windows <c>*.exe</c> / macOS
/// <c>.app</c> bundle) by <see cref="SlicerExecutableResolver"/> at discovery time — this strategy
/// only builds the argument list. The exact flag surface is validated manually against the real
/// binary, the same carve-out as the rest of this CLI-invocation layer (never a CI gate).
/// </summary>
public sealed class SuperSlicerInvocation : ISlicerInvocation
{
    public bool SupportsEngine(SlicerEngine engine) => engine == SlicerEngine.SuperSlicer;

    public SlicerProcessInvocation BuildInvocation(
        AvailableSlicer slicer, string modelFilePath, string materialType, decimal nozzleDiameterMm, string workDirectoryPath)
    {
        var outputPath = Path.Combine(workDirectoryPath, "output.gcode");

        var arguments = $"--slice --filament-type \"{EscapeMaterial(materialType)}\" " +
            $"--nozzle-diameter {nozzleDiameterMm.ToString(CultureInfo.InvariantCulture)} " +
            $"--export-gcode \"{outputPath}\" \"{modelFilePath}\"";

        return new SlicerProcessInvocation(slicer.ExecutablePath, arguments, outputPath, ProducesGCodeBearingThreeMf: false);
    }

    private static string EscapeMaterial(string materialType) => materialType.Replace("\"", "\\\"");
}
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~SlicerInvocationTests"`
Expected: PASS, 4 tests.

- [ ] **Step 7: Commit**

```bash
git add api/src/OpenFarm.Application/Parts/ISlicerInvocation.cs \
        api/src/OpenFarm.Infrastructure/Parts/SlicerInvocation/PrusaSliceFamilyInvocation.cs \
        api/src/OpenFarm.Infrastructure/Parts/SlicerInvocation/SuperSlicerInvocation.cs \
        api/tests/OpenFarm.Infrastructure.Tests/Parts/SlicerInvocationTests.cs
git commit -m "fix(slicing): build OrcaSlicer/Bambu Studio's real CLI form

--export-gcode and --nozzle-diameter don't exist on either engine's real
binary (exit 254, 'Invalid option --export-gcode'). Both only accept
nozzle diameter and filament type via JSON settings files loaded through
--load-settings/--load-filaments, and export via --export-3mf, which
writes a gcode-bearing 3MF rather than a plain .gcode file."
```

---

### Task 2: Teach `CliSlicerEngine` to read a gcode-bearing 3MF

**Files:**
- Modify: `api/src/OpenFarm.Infrastructure/Parts/CliSlicerEngine.cs`
- Test: `api/tests/OpenFarm.Infrastructure.Tests/Parts/CliSlicerEngineTests.cs`

**Interfaces:**
- Consumes: `SlicerProcessInvocation.OutputPath`/`ProducesGCodeBearingThreeMf` from Task 1.
- Produces: `internal static Task<string> CliSlicerEngine.ReadEmbeddedPlateGCodeAsync(string threeMfPath, CancellationToken cancellationToken)` —
  `internal` (not `private`) so `OpenFarm.Infrastructure.Tests` can unit-test it directly via the
  existing `InternalsVisibleTo` in `OpenFarm.Infrastructure.csproj:39` (same pattern as
  `BambuCertificateTrust`) without needing a real slicer subprocess.

- [ ] **Step 1: Add failing tests for the new helper**

Add `using System.IO.Compression;` to the top of
`api/tests/OpenFarm.Infrastructure.Tests/Parts/CliSlicerEngineTests.cs`, then add these two `[Fact]`
methods inside the `CliSlicerEngineTests` class (anywhere after the existing two tests):

```csharp
    [Fact]
    public async Task ReadEmbeddedPlateGCodeAsync_ThreeMfWithPlateOneGCode_ReturnsItsContent()
    {
        var threeMfPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.gcode.3mf");
        using (var archive = ZipFile.Open(threeMfPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("Metadata/plate_1.gcode");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("; nozzle_diameter = 0.4\nG28\n");
        }

        try
        {
            var gcode = await CliSlicerEngine.ReadEmbeddedPlateGCodeAsync(threeMfPath, CancellationToken.None);

            gcode.Should().Contain("nozzle_diameter = 0.4");
        }
        finally
        {
            File.Delete(threeMfPath);
        }
    }

    [Fact]
    public async Task ReadEmbeddedPlateGCodeAsync_ThreeMfMissingPlateOneGCode_ThrowsSlicingFailedException()
    {
        var threeMfPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.gcode.3mf");
        using (ZipFile.Open(threeMfPath, ZipArchiveMode.Create))
        {
            // Deliberately empty — no Metadata/plate_1.gcode entry.
        }

        try
        {
            var act = async () => await CliSlicerEngine.ReadEmbeddedPlateGCodeAsync(threeMfPath, CancellationToken.None);

            await act.Should().ThrowAsync<SlicingFailedException>();
        }
        finally
        {
            File.Delete(threeMfPath);
        }
    }
```

- [ ] **Step 2: Run the tests to confirm they fail to compile**

Run: `dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~CliSlicerEngineTests"`
Expected: build error — `CliSlicerEngine.ReadEmbeddedPlateGCodeAsync` does not exist yet.

- [ ] **Step 3: Update `CliSlicerEngine.cs`**

In `api/src/OpenFarm.Infrastructure/Parts/CliSlicerEngine.cs`, add `using System.IO.Compression;` to the
top of the file (alongside the existing `using System.Diagnostics;`).

Replace:

```csharp
            var outputPath = Path.Combine(workDirectory.FullName, "output.gcode");
            var processInvocation = invocationStrategy.BuildInvocation(available, inputPath, materialType, nozzleDiameterMm, outputPath);
```

with:

```csharp
            var processInvocation = invocationStrategy.BuildInvocation(available, inputPath, materialType, nozzleDiameterMm, workDirectory.FullName);
```

Replace:

```csharp
                if (process.ExitCode != 0 || !File.Exists(outputPath))
                {
                    var stderr = await process.StandardError.ReadToEndAsync(CancellationToken.None);
                    throw new SlicingFailedException($"The {slicer.Engine} CLI exited with code {process.ExitCode}: {stderr}");
                }

                var gcode = await File.ReadAllTextAsync(outputPath, cancellationToken);
```

with:

```csharp
                if (process.ExitCode != 0 || !File.Exists(processInvocation.OutputPath))
                {
                    var stderr = await process.StandardError.ReadToEndAsync(CancellationToken.None);
                    throw new SlicingFailedException($"The {slicer.Engine} CLI exited with code {process.ExitCode}: {stderr}");
                }

                var gcode = processInvocation.ProducesGCodeBearingThreeMf
                    ? await ReadEmbeddedPlateGCodeAsync(processInvocation.OutputPath, cancellationToken)
                    : await File.ReadAllTextAsync(processInvocation.OutputPath, cancellationToken);
```

Add this method to the `CliSlicerEngine` class, next to `TryKill` near the bottom:

```csharp
    /// <summary>
    /// OrcaSlicer/Bambu Studio's <c>--export-3mf</c> writes a gcode-bearing 3MF, not a plain .gcode
    /// file — the real G-code for the first (only, today) plate lives inside it as a zip entry.
    /// Internal (not private) so InternalsVisibleTo lets Infrastructure.Tests exercise this directly
    /// without a real slicer subprocess, same rationale as BambuCertificateTrust.
    /// </summary>
    internal static async Task<string> ReadEmbeddedPlateGCodeAsync(string threeMfPath, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(threeMfPath);
        var entry = archive.GetEntry("Metadata/plate_1.gcode")
            ?? throw new SlicingFailedException("The exported 3MF did not contain Metadata/plate_1.gcode.");

        using var reader = new StreamReader(entry.Open());
        return await reader.ReadToEndAsync(cancellationToken);
    }
```

- [ ] **Step 4: Run the full test file and confirm everything passes**

Run: `dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~CliSlicerEngineTests"`
Expected: PASS, 4 tests (the original 2 plus the 2 new ones).

- [ ] **Step 5: Commit**

```bash
git add api/src/OpenFarm.Infrastructure/Parts/CliSlicerEngine.cs \
        api/tests/OpenFarm.Infrastructure.Tests/Parts/CliSlicerEngineTests.cs
git commit -m "fix(slicing): unzip gcode-bearing 3MF output from OrcaSlicer/Bambu Studio

CliSlicerEngine used to always read outputPath as plain-text G-code.
OrcaSlicer/Bambu Studio's --export-3mf writes a zip archive with the
real G-code at Metadata/plate_1.gcode inside it, so branch on the
strategy's ProducesGCodeBearingThreeMf and unzip when needed."
```

---

### Task 3: Full-solution verification and manual real-CLI validation note

**Files:** none (verification only).

- [ ] **Step 1: Run the full backend test suite**

Run: `dotnet test api/OpenFarm.slnx`
Expected: PASS, no regressions in any other project (`OpenFarm.Domain.Tests`,
`OpenFarm.Application.Tests`, `OpenFarm.Api.IntegrationTests` included).

- [ ] **Step 2: Format check**

Run: `dotnet format api/OpenFarm.slnx --verify-no-changes`
Expected: no output, exit code 0. If it reports changes, run `dotnet format api/OpenFarm.slnx` and
re-run Step 1.

- [ ] **Step 3: Release build (nullable/warnings-as-errors gate)**

Run: `dotnet build api/OpenFarm.slnx --configuration Release`
Expected: `Build succeeded.`

- [ ] **Step 4: Report the manual real-CLI validation carve-out**

This plan's tests prove the argument string and 3MF-unzip logic are correct against what the CLI
reference documents — they cannot prove OrcaSlicer's actual binary accepts the generated
`machine_settings.json`/`filament_settings.json` content, because that validation is this repo's
established manual carve-out (not a CI gate, per `quickstart.md`). Tell the user: rebuild/restart the
API against the bundled OrcaSlicer in `.slicers/` and upload the same STL that originally failed;
confirm it now produces a part with a parseable estimate/thumbnail instead of exit code 254. If Orca's
CLI rejects the minimal settings JSON (e.g. it wants more machine-profile keys than just
`nozzle_diameter`), that's the next thing to fix — report the exact new error rather than guessing
further fields preemptively.
