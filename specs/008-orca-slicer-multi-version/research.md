# Phase 0 Research: Multi-Engine, Multi-Version Slicer Management

All Technical Context unknowns were resolvable from the existing Phase 4 code and the spec's
Clarifications; none remain marked NEEDS CLARIFICATION. Each decision below records what was chosen, why,
and the alternatives rejected.

## §1 — Where the "available slicer" set comes from (discovery vs. configuration)

**Decision**: The set of usable slicers is built **only** by a startup filesystem scan of a bundled slicer
root directory (`SlicerBundle:RootDirectory`), producing an in-memory `IAvailableSlicerCatalog` singleton.
Presence of a leaf directory *is* availability — there is no execution/probe (FR-002). No path, PATH, or
binary is ever surfaced to a user or admin (FR-001/FR-020).

**Directory convention** (the only "path" knowledge, entirely backend-internal):

```
<RootDirectory>/<engineSlug>/<version>/<platformSlug>/…extracted engine tree…
   engineSlug   ∈ orcaslicer | bambustudio | superslicer
   version      = exact upstream release string, verbatim (e.g. "2.2.0", "2.5.59.13")
   platformSlug ∈ linux | windows | macos
```

Each present `<engine>/<version>/<platform>` leaf becomes one `AvailableSlicer(engine, version, platform)`.
The executable *within* the leaf is resolved by a per-engine/per-platform convention (see §4), never stored
or shown.

**Rationale**: Matches the clarified product decision exactly — the only user-facing concepts are engine
and version, discovered by the backend. A directory walk is cheap, needs no DB, and makes the bundle the
single source of truth (rebuilt every boot, so no durability concern). The layout is self-describing, so a
provisioning tool and the scanner agree without a side-channel index file.

**Alternatives rejected**: (a) DB-backed slicer registry — adds migration/seed churn and a sync problem
against what's actually on disk, for zero benefit since the bundle already is the truth. (b) A manifest
file *read by the app at runtime* to enumerate slicers — redundant with the directory itself and a second
thing to keep consistent. (c) Config-listed explicit paths — reintroduces exactly the hard-coded-path
anti-pattern this feature exists to remove.

## §2 — Catalog lifetime and the "slice requested during discovery" race

**Decision**: `AvailableSlicerCatalog` is a **singleton eagerly populated during application startup**
(the scan runs before `app.Run()` returns to serving), so by the time any request is accepted the catalog
is complete. If a slice is nonetheless requested against an empty/incomplete catalog (edge case), it fails
promptly with a "no slicer available" `ProblemDetails` (FR-006) rather than blocking. The catalog exposes
an immutable snapshot; there is no runtime rescan (the bundle is fixed for a running install).

**Rationale**: Eager startup init removes the race the spec calls out ("a slice is requested while the
backend is still discovering") without a readiness state machine. Singleton + immutable snapshot is
thread-safe for the concurrent slicing workers with no locking.

**Alternatives rejected**: Lazy first-use scan (reintroduces the race and adds latency to the first
slice); a `BackgroundService` that populates asynchronously (would let requests briefly see an empty
catalog — the exact edge we want closed). A filesystem watcher for hot-add is unnecessary: the bundle does
not change under a running install, and append-only means a new version arrives only via a new deployment.

## §3 — Version identity and cross-engine "newest" comparison

**Decision**: A version is the **exact upstream release string, stored verbatim** on entities and shown
verbatim in pickers (FR-005; no friendly-label scheme). Ordering ("newest") is computed by a tolerant
`SlicerVersionComparer` that parses a dotted numeric release (`2.2.0`, `2.5.59.13`, Bambu's
`01.10.00.00`) into a variable-length list of integer components and compares component-wise, shorter
padded with zeros; a non-numeric suffix (e.g. an RC tag) orders **before** the same release without it and
is compared lexically only as a tiebreak. Comparison is **within a single engine** only — versions are
never ordered across engines.

**Rationale**: The three engines all publish dotted-numeric releases but with differing component counts,
so a fixed `System.Version` (max 4 components, rejects 1-component) is too rigid; a hand-rolled
component-list comparer covers all three. "Newest within an engine" is all FR-003 needs — cross-engine
preference is handled by the explicit engine preference order in §5, not by comparing "2.2.0" to
"2.5.59.13".

**Alternatives rejected**: `System.Version` (can't represent all shapes, throws on odd inputs); SemVer2
libraries (SuperSlicer's `2.5.59.13` is not semver — four numeric fields); string sort (`"2.10.0" <
"2.9.0"` lexically — wrong). This is deterministic, well-specified logic → **TDD** (Constitution II).

## §4 — Per-engine CLI invocation (the SuperSlicer divergence)

**Decision**: Introduce `ISlicerInvocation` — builds the process `FileName` + argument list for a given
resolved `AvailableSlicer` + slice inputs (model file, material, nozzle diameter, output path). Two
implementations, selected by engine:

- `PrusaSliceFamilyInvocation` — OrcaSlicer **and** Bambu Studio (shared PrusaSlicer-derived CLI). This is
  today's `CliSlicerEngine` argument string (`--slice 0 --load-filaments … --nozzle-diameter … --export-gcode …`),
  lifted out unchanged.
- `SuperSlicerInvocation` — SuperSlicer's PrusaSlicer-fork CLI form (its slice/export flags differ; the
  exact flag surface is validated manually against the binary, same carve-out as the existing real-CLI
  path). Encapsulated so the divergence lives in one class.

Executable resolution within a leaf is also per engine/platform (AppRun on extracted Linux AppImage;
`*.exe` console binary on Windows; `.app/Contents/MacOS/…` on macOS), owned by the invocation/entry
resolver, never surfaced.

**Rationale**: FR-009a requires each engine invoked with its correct command form while producing the same
downstream G-code metadata the pipeline already parses. A strategy keyed by engine isolates the fork
divergence and keeps `CliSlicerEngine` as the orchestration shell (temp dir, process lifetime, timeout,
failure→`SlicingFailedException`) it already is. Adding a 4th engine later = one more strategy (FR-022).

**Alternatives rejected**: One giant `switch` inside `CliSlicerEngine` (mixes orchestration with
per-engine flag knowledge, grows unbounded); shelling out via a per-engine shell script in the bundle
(reintroduces path/script surface and a platform-portability burden the app shouldn't own).

## §5 — Default `(engine, version)` resolution and the out-of-the-box fallback

**Decision**: The organization stores an **optional** default as an `(engine, version)` pair (nullable).
Effective default is resolved **at read time** against the live catalog:

1. If an explicit `(engine, version)` is set **and still present** in the catalog for the host platform →
   use it. (It can never become absent — append-only, FR-008a — but the presence check is kept as the
   single validation path.)
2. Otherwise auto-default: prefer **OrcaSlicer**'s newest host-platform version; if no OrcaSlicer is
   bundled, the newest version of any available engine (engine tiebreak order OrcaSlicer → BambuStudio →
   SuperSlicer). (FR-003)
3. If the catalog is empty for the host platform → there is no effective default; uploads that require
   slicing fail with "no slicer available" (FR-006/FR-011).

Resolving at read time (not persisting the fallback) keeps the system usable out of the box and means a
later-bundled newer version is picked up without a data migration.

**Rationale**: FR-003 spells out exactly this preference-with-fallback. Storing only the *explicit* choice
(and computing the fallback) avoids a stale persisted default and needs no write on first boot.

**Alternatives rejected**: Persisting a concrete default at seed time (goes stale when a newer version is
bundled; needs a migration/backfill); making the admin set a default before the product works (violates
"always usable out of the box").

## §6 — Recording engine + version for reproducibility

**Decision**: Add `SlicerVersionUsed` (string, nullable) beside the existing `SlicerEngineUsed` on
`PartProcessingJob`, and `SlicerVersionUsed` beside `SlicerEngineOverride` on `Part` — **renaming the
Part's recorded field intent** from "override" to "the engine+version actually used" (the override is the
*input*; the recorded pair is the *result*). Both are set at slice time from the resolved `SlicerRef`.
Existing rows (sliced under the old single OrcaSlicer path) keep `NULL` version — displayed as
"unknown/legacy", no backfill required (spec Assumptions).

**Rationale**: FR-005 requires the exact engine **and** version on both job and part; a plain verbatim
string column is the simplest faithful store (no comparer needed at rest — ordering is a UI/registry
concern). Nullable preserves historical rows without inventing a version for them.

**Alternatives rejected**: A foreign key to a DB slicer table (no such table by §1); encoding engine+version
into one column (loses the clean `HasConversion<string>()` enum mapping already used for engine).

## §7 — Availability + platform validation gate

**Decision**: A single `ResolveSlicerSelection` application service turns a requested selection (explicit
override, else the effective default) into a validated `SlicerRef`, throwing a typed domain exception
(→ RFC 7807) when the `(engine, version)` is **not present for the host platform**. This gate fronts both
"set as default" and "slice this part" (FR-006/FR-007/FR-010/FR-011). Because the catalog only ever holds
host-platform-usable entries the app is willing to run, a mismatch is caught before any process starts —
never a hang.

**Rationale**: Centralizing the presence+platform check in one service keeps the rule identical for the
two call sites (default-setting endpoint and the add/replace pipeline) — Constitution III's "same rule,
same decision on both sides." Deterministic, well-specified → **TDD**.

**Alternatives rejected**: Duplicating the check in each endpoint (drift risk); validating only at slice
time (lets an admin set an invalid default that fails later instead of at set time — FR-007 wants it
prevented up front).

## §8 — Dev/build-time provisioning tool: language, integrity, idempotency

**Superseded invocation mechanism**: The pipeline below still lives in `tools/provision-slicers`, but it
is now a plain **class library** with no CLI entry point of its own — invoked via the repo's NUKE build
(`build/Build.cs`'s `ProvisionSlicers` Target, run as `./build.sh ProvisionSlicers`) rather than
`dotnet run --project tools/provision-slicers` directly. This folds provisioning into the same build
definition used for backend/frontend build+test in CI (`.github/workflows/ci.yml`), standardizing "the
one command that runs the same way locally and in CI" per the project's stated direction. The pipeline
logic, its tests, and everything below this line are unchanged.

**Decision**: A standalone **.NET class library** `tools/provision-slicers`, invoked via NUKE
(cross-platform on the SDK the repo already requires). It:

- reads `slicers.manifest.json` (engine, version, platforms — see contract schema);
- for each `(engine, version, platform)`, resolves the correct GitHub release asset via each engine's
  official repo (`SoftFever/OrcaSlicer`, `bambulab/BambuStudio`, `supermerill/SuperSlicer`) using a
  per-engine asset-name resolver (`EngineReleaseSources`);
- downloads, **verifies SHA-256** against `slicers.lock.json` (pinned per entry; on first fetch of a new
  entry it records the computed digest into the lockfile), then extracts into the §1 bundle layout;
- is **idempotent** — a present, digest-matching leaf is skipped and reported "already present" (FR-015);
- reports **per-entry** success/failure and never lets one failed/unavailable entry abort the others
  (FR-015/FR-017), exiting non-zero if any required entry failed.

**Rationale**: A .NET tool keeps everything in one toolchain, runs identically on Linux/Windows/macOS,
and can unit-test its pure logic (manifest parse, asset resolution, idempotency, integrity) with the same
xUnit stack. GitHub does not universally publish per-asset checksums, so a repo-committed
`slicers.lock.json` gives reproducible integrity verification (FR-014) and turns the manifest into a
desired-state / lock pair a maintainer can reconcile (FR-017). Keeping it a **separate project** guarantees
no runtime code path can reach GitHub-fetching logic (FR-018).

**Alternatives rejected**: A Bun/TypeScript script (web already uses Bun) — viable, but splits slicer
logic across two toolchains and can't share the version comparer / engine constants with the backend; a
bash + PowerShell pair (double the maintenance, no shared tests); an in-app "download on demand" action
(explicitly out of scope, and forbidden by FR-018).

## §9 — Bundling in the shipped artifact (Dockerfile / packaging)

**Decision**: Replace the Dockerfile's single hard-coded `ARG ORCASLICER_VERSION` install + symlink with
a build step that runs `tools/provision-slicers` against `slicers.manifest.json` for the image's target
platform, populating `<RootDirectory>` (the container sets `SlicerBundle:RootDirectory` to it). The image
no longer creates `/usr/local/bin/orca-slicer-cli`. The shipped artifact is platform-specific (FR-018,
spec Assumptions) — a Linux image bundles Linux builds; Windows/macOS packaging runs the same tool for
their platform.

**Rationale**: The provisioning tool becomes the single mechanism for both a developer's local checkout and
the release image, so "one documented script" (SC-006) is literally true. The bundle-dir contract (§1)
means the running app discovers whatever the build placed, no per-path config.

**Alternatives rejected**: Keeping the ad-hoc `curl | --appimage-extract | symlink` per engine in the
Dockerfile (doesn't scale to 3 engines × N versions × 3 platforms, and duplicates asset-resolution logic
the tool already owns).

## §10 — Frontend surfaces

**Decision**: (a) A new **Slicing Defaults** settings page (card/link added to `SettingsPage`, which today
only links to Connection Secrets) listing available `(engine, version, platform)` slicers and letting an
admin pick the org default — gated on `settings:slicing-defaults:update`. (b) An **optional** engine+version
override control in `AddOrReplacePartDialog` (today it exposes no engine choice at all), defaulting to the
org default, only shown for files that require slicing. Both consume the Orval-generated client; the
available list comes from the settings/available-slicers endpoint.

**Rationale**: Realizes FR-003/FR-004 in the UI using the existing shadcn/ui + react-hook-form + TanStack
Query conventions (Constitution III). Keeping the per-part override optional preserves the "most uploads
take the default with no decision" clarification.

**Alternatives rejected**: A standalone top-level Slicers nav section (over-weights an admin-rare setting);
forcing an engine/version choice on every upload (contradicts the default-first clarification).

## §11 — Migration & backend wiring specifics

**Decision**: One hand-authored EF migration `AddSlicerVersionTracking` (no `dotnet ef` tooling in this
sandbox — follow the existing `[DbContext]`/`[Migration]` attribute pattern from `AddPartsCatalog` /
`AddOrganizationSlicingDefaults`) adding: `PartProcessingJobs.SlicerVersionUsed text NULL`,
`Parts.SlicerVersionUsed text NULL`, `Organizations.DefaultSlicerVersion text NULL`. `SlicerOptions`
(path-centric) is replaced by `SlicerBundleOptions { RootDirectory }`; `Program.cs` swaps the
`SlicerOptions` registration, registers `IAvailableSlicerCatalog` as an eager singleton (scan at startup),
registers `ISlicerInvocation` strategies, and keeps `ISlicerEngine`/`CliSlicerEngine` (now catalog-driven).
`appsettings.json`'s `Slicing` section drops `OrcaSlicerCliPath`/`BambuStudioCliPath` for
`SlicerBundle:RootDirectory` (retains `MaxConcurrentSlicingOperations`).

**Rationale**: Matches the repo's established hand-authored-migration workflow and one-migration-per-PR
rule (Constitution / CLAUDE.md). The options rename makes the "no path" guarantee structural — there is no
longer a path property to set.

**Alternatives rejected**: Reusing `SlicerOptions` with the path fields left dormant (leaves a
foot-gun path knob in config, contradicting FR-020); multiple migrations (violates one-per-PR for a single
cohesive schema delta).
