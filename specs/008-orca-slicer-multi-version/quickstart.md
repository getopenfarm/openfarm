# Quickstart & Validation: Multi-Engine, Multi-Version Slicer Management

Runnable scenarios that prove the feature end-to-end. References the API contract
(`contracts/slicer-management-api.md`), the manifest schema (`contracts/slicer-manifest.schema.json`), and
the data model (`data-model.md`) rather than restating them. Commands assume the repo conventions in
`CLAUDE.md`.

## Prerequisites

- .NET 10 SDK (`api/global.json`), Bun, Docker (for Testcontainers / local stack).
- `ConnectionStrings__Default` set for backend tests (see `.env.example`).
- A writable bundle directory for local dev, exported as `SlicerBundle__RootDirectory`
  (e.g. `export SlicerBundle__RootDirectory=$PWD/.slicers`).

## Setup — provision a bundle from the manifest (Story 3)

1. Edit `slicers.manifest.json` to declare at least two entries spanning **two engines** for your host
   platform. Each entry's `assets` object maps platform → the **exact download URL**, copied verbatim
   from that engine's GitHub releases page — the tool never derives or guesses a filename from a
   pattern, since asset naming can and does change between releases (validate against
   `contracts/slicer-manifest.schema.json`).
2. Run the provisioning tool:

   ```bash
   dotnet run --project tools/provision-slicers -- \
       --manifest slicers.manifest.json \
       --out "$SlicerBundle__RootDirectory" \
       --platform Linux
   ```

   **Expected**: the tool first prints how many entries it found for the platform, then for each one
   prints its own progress as it happens (resolving → downloading, with byte count → verifying SHA-256
   → extracting) rather than staying silent until the end — a large binary download with no output in
   between is otherwise indistinguishable from a hang. Each declared `(engine, version, Linux)` is
   fetched from the manifest-supplied URL, SHA-256-verified against `slicers.lock.json`, and extracted
   to `<out>/<engineSlug>/<version>/linux/…` (research §1). Per-entry success/failure is printed; an
   unavailable/failed entry does not abort the others (FR-015/FR-017). A missing lockfile digest is
   computed and recorded on first fetch. A final summary line reports overall success/failure.

3. Re-run the exact same command → every entry reports **"already present"** and nothing re-downloads
   (idempotent, FR-015 / SC-004).
4. Add a bogus entry (nonexistent URL) and re-run → that one entry fails with a clear
   engine/version/platform message (a 404 download failure); the real entries still complete (SC-004).
5. For a `MacOs` entry's `.dmg` asset: running the tool **on macOS** actually mounts the disk image
   (`hdiutil`) and copies its `.app` bundle into the leaf — real extraction, not a manual step. Running
   it anywhere else fails that entry with an explicit "requires running this tool on macOS" message
   (the raw `.dmg` is still written to the leaf so nothing downloaded is lost); a maintainer completes
   it by hand on an actual Mac. Windows `.zip` builds need no such carve-out — extraction is a plain
   `ZipFile.ExtractToDirectory`, and the app's executable lookup searches the leaf recursively so a
   zip that wraps everything in one folder still resolves correctly.

## Story 1 — Slice with a backend-discovered slicer, no path anywhere

1. Start the API with `SlicerBundle__RootDirectory` pointing at the provisioned bundle (local stack:
   `cd infra && docker compose up`, which provisions the bundle in `infra/docker/api.Dockerfile`).
2. `GET /api/v1/settings/slicing-defaults` → confirm `available[]` lists the provisioned
   `(engine, version, platform)` entries with **no path field anywhere**, and `default` is auto-populated
   with the newest OrcaSlicer version, `isExplicit: false` (FR-003).
3. Upload a part that requires slicing (`POST /api/v1/parts` with just `File`) → `202`; the job (and, on
   confirm, the Part) records the resolved engine+version — check `slicer.engine`/`slicer.version` on
   `GET /api/v1/part-processing-jobs/{id}` and `GET /api/v1/parts/{id}` (FR-005 / SC-001).
4. Point `RootDirectory` at an **empty** dir and restart → an upload that requires slicing returns a
   `422`/`409` "no slicer available" ProblemDetails within seconds, and no job is left stuck Processing
   (FR-006 / SC-002).
5. Corrupt a bundled binary (truncate the leaf's executable) and upload → the slice fails **at slice time**
   with a `SlicingFailedException`-derived message (no pre-validation, FR-002), and the job reaches the
   terminal Failed status via the existing path — never a hang (Constitution V).

## Story 2 — Multiple engines/versions side by side, choose one

1. With ≥2 engines available, set the org default to a specific pair:
   `PUT /api/v1/settings/slicing-defaults { "engine":"OrcaSlicer","version":"2.2.0" }` → `200`,
   `default.isExplicit: true`; confirm a `SlicingDefaultsUpdated` activity-log entry was written (FR-019).
2. Upload part A with no override → sliced with the default `(OrcaSlicer, 2.2.0)`.
3. Upload part B with `SlicerEngine=SuperSlicer` + `SlicerVersion=2.5.59.13` → sliced with that pair; the
   org default is unchanged (US2 scenario 2). Each part records the exact pair it was sliced with (SC-001).
4. Submit an upload naming an `(engine, version)` **not** in the bundle (simulate a stale client) → `422`
   ProblemDetails naming the pair, **before** processing begins (US2 scenario 3 / FR-006).
5. Change the org default, then re-open an already-processed part → it still shows its **original**
   recorded engine+version (FR-008 / US2 scenario 4).
6. Each of the three engines, when bundled, slices a representative part and yields the estimate/thumbnail/
   filament outputs the pipeline parses — proving the per-engine invocation form is correct
   (FR-009a / SC-007). (Real-CLI validation is manual, same carve-out as the existing `CliSlicerEngine`.)

## Automated test coverage (what CI asserts)

- **Unit (xUnit + FluentAssertions, TDD):** `SlicerVersionComparer` ordering across the three release
  shapes; newest-available default resolution incl. the OrcaSlicer-preference and empty-catalog cases
  (research §5); `ResolveSlicerSelection` availability+platform gate — accept vs.
  `SlicerUnavailableException` (research §7); provisioning-tool manifest parse / asset resolution /
  idempotency / integrity logic (research §8).
- **Infra (fake/fixture, test-after):** `BundledSlicerScanner` builds the expected `AvailableSlicer` set
  from a temp directory tree, incl. duplicate-leaf de-duplication and platform filtering;
  `PrusaSliceFamilyInvocation` vs. `SuperSlicerInvocation` produce their expected argument lists;
  `CliSlicerEngine` still routes every failure to `SlicingFailedException`.
- **Integration (`WebApplicationFactory` + Testcontainers.PostgreSql):** GET/PUT slicing-defaults incl.
  the `422` unavailable case; add/replace resolution paths (default, engine-only, full pair, stale-pair
  `422`, no-slicer `422`); recorded engine+version present on job and part reads; the migration applies and
  the new nullable columns round-trip.
- **Frontend (Vitest + RTL):** the Slicing Defaults settings page renders the available list and submits a
  default; the Add/Replace dialog's optional override defaults to the org default and only appears for
  requires-slicing files.
- **Not automated (manual, documented):** real GitHub download in the provisioning tool; real per-engine
  CLI slicing against actual binaries (mirrors the physical-hardware / real-CLI carve-out in
  Constitution II).

## Done when

- Two engines are simultaneously available and each slices a part that records its exact engine+version
  (SC-001).
- Every unavailable-slicer request fails actionably in seconds with zero stuck jobs (SC-002).
- The same manifest provisions the bundle and a deployed install slices from it with no path config and no
  runtime download (SC-003 / SC-006), and re-running is idempotent (SC-004).
- A part re-sliced with its recorded engine+version reproduces an equivalent estimate (SC-005), and all
  three engines produce parseable outputs (SC-007).
