# Feature Specification: Multi-Engine, Multi-Version Slicer Management

**Feature Branch**: `008-orca-slicer-multi-version`

**Created**: 2026-07-21

**Status**: Draft

**Input**: User description: "Currently the part feature is implemented but it's not functional yet, because the orca slicer CLI is hard coded and it only support single version. I think we have to re-plan to support multiple versions, multi platform (linux,windows,macos), and might need to have a way to download orca slicers from their github repo. So maybe the software just configure path? Then we have a script to download various versions from github, also maybe we need a json file to determine which version need to download? Please review and plan"

## Context & Problem Statement

Phase 4 (Part Library) shipped in-app slicing, but the slicer integration is not usable in a real deployment:

- Each slicer engine's CLI is referenced by a **single hard-coded filesystem path** (`Slicing:OrcaSlicerCliPath`, default `/usr/local/bin/orca-slicer-cli`). There is **no concept of a slicer version**, and only two engines are even modelled.
- Exactly **one OrcaSlicer version** is baked into the Linux container image at build time. There is no way to run a second version side by side, and nothing works on Windows or macOS self-hosted installs.
- The **version actually used to slice is not recorded**, so a re-slice or an audit cannot reproduce the original result. This matters because a slicer's CLI arguments, G-code metadata, and slicing output change between versions, and a 3MF/project file authored against a particular slicer version (or a different engine entirely) can slice incorrectly — or fail — under a mismatched one.

**Supported engines:** This feature manages three slicer engines — **OrcaSlicer**, **Bambu Studio**, and **SuperSlicer** — uniformly. Each is multi-version and multi-platform; a specific slice is identified by an **(engine, version)** pair. OrcaSlicer and Bambu Studio share PrusaSlicer-derived CLI conventions; SuperSlicer is a separate PrusaSlicer fork whose CLI differs, so the system must invoke each engine with its own correct command form.

**Delivery model (decided):** Slicer engines are **bundled with the application** — the shipped artifact includes each supported engine's CLI and the version binaries it supports, so a deployed install can slice out of the box with no runtime download and no operator install step. A **dev/build-time provisioning tool** (a script driven by a JSON version manifest) fetches the desired (engine, version) builds from each engine's GitHub releases into the bundle: used by developers locally today, and by the packaging/release process to assemble the shipped artifact later. The **running application never reaches out to GitHub** — it only discovers and uses the versions bundled alongside it (or, in a dev checkout, the versions the script placed).

An operator running a print farm needs to (a) have one or more specific (engine, version) slicers already available to the app (bundled at ship time, or populated by the dev script during development), (b) choose which engine and version a given part is sliced with, and (c) trust that the recorded engine+version makes re-slicing reproducible. This feature re-plans slicer integration to deliver that.

This specification does not itself re-slice existing parts, redesign the Part Library UI, or change how estimates are parsed — it makes *slicer engines and versions a bundled, selectable, recorded capability* so the Part Library becomes functional.

## Clarifications

### Session 2026-07-21

- Q: Should users ever deal with slicer binaries, filesystem paths, or system PATH? → A: No. Users have no concept of a binary or its location. The backend scans the bundled slicer directory at deploy/startup, populates the set of available versions itself, and the only user-facing concept is a **version** the user picks from. No manual path entry exists anywhere in the product.
- Q: What happens to a part whose recorded slicer version is not bundled in a later deployment? → A: Not applicable by design — once a version is supported it is bundled **forever** (the version set is append-only across releases; a shipped version is never dropped). Therefore a recorded version is always still available, and there is no "version disappeared" case to handle.
- Q: At what granularity does a user choose a slicer version? → A: Both an org-wide default (applied automatically, admin-set) and an optional per-part override at upload/replace time. Most uploads take the default with no decision; the override exists for parts authored against a different version.
- Q: What identifies a "version" to the user? → A: The exact OrcaSlicer release version as published upstream (e.g. "2.2.0"), displayed in the picker and recorded on the part verbatim — no separate friendly-label scheme.
- Q: When/how does the backend validate a bundled version? → A: It does not validate. The backend simply **discovers** the versions present in the bundle at startup — a version's availability means it is present. No executing binaries to verify runnability, no self-report probing. A genuinely broken binary surfaces as a normal, prompt slice failure at slice time (existing slicer-failure handling), not a pre-flight check.
- Q: With multiple versions bundled and no default explicitly set, which version does an upload use? → A: Auto-default to the newest (highest) bundled version until an admin sets one explicitly. The system is always usable out of the box; the admin can override the choice.
- Q: Which slicer engines are in scope? → A: Three — **OrcaSlicer, Bambu Studio, and SuperSlicer** — managed uniformly, each multi-version and multi-platform. A slice is identified by an (engine, version) pair; the org default and per-part override both select engine + version. (Supersedes the earlier "OrcaSlicer only" decision.) Each engine is invoked with its own correct CLI form (SuperSlicer's differs from the OrcaSlicer/Bambu Studio family).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Slice with a bundled, backend-discovered slicer (engine + version) (Priority: P1)

As an operator, I upload a part and it is sliced with a known slicer engine and version that the backend already discovered from the bundle — without me knowing or touching any binary or path — so that slicing works predictably instead of failing.

**Why this priority**: This is the minimum that makes the Part Library functional at all — without at least one discovered (engine, version), no part that requires slicing can ever be processed. Everything else builds on this.

**Independent Test**: With one supported engine+version present in the bundle, upload a part that requires slicing and confirm it slices successfully and the resulting part records the engine and version used. With the bundled binary absent/broken, confirm the upload fails fast with a clear message instead of hanging.

**Acceptance Scenarios**:

1. **Given** the backend has discovered the bundle at startup, **When** a supported engine's version is present, **Then** that (engine, version) is shown to users as "available" with its exact release version, its engine, and the platform it applies to — and no path is shown anywhere.
2. **Given** exactly one available (engine, version) exists, **When** an operator uploads a part that requires slicing, **Then** the part is sliced with that engine+version, using that engine's correct CLI form, and the engine and version are stored on the resulting part and its processing job.
3. **Given** a bundled version whose binary turns out to be broken (not caught up-front, since there is no pre-validation), **When** an operator uploads a part that uses it, **Then** the slice attempt fails promptly at slice time with a clear message, and no job is left stuck in a processing state.
4. **Given** no slicer version of any engine is present in the bundle, **When** an operator uploads a part that requires slicing, **Then** the app rejects/queues the request with a clear "no slicer available" explanation rather than silently accepting it.

---

### User Story 2 - Run multiple engines and versions side by side and choose one (Priority: P1)

As a farm administrator, I have several slicers available (multiple engines — OrcaSlicer, Bambu Studio, SuperSlicer — and multiple versions per engine, all discovered from the bundle) and set a default (engine, version), and an operator can override which engine+version a particular part is sliced with, so that parts authored against different slicers each slice correctly.

**Why this priority**: The single-engine, single-version limitation is the core defect the user called out. Being able to hold several engines/versions and pick per-part is what makes the feature "multi-engine, multi-version" rather than just "configurable path". It reuses the existing per-part engine-override pattern, so it is a natural P1 companion to Story 1.

**Independent Test**: With versions of two different engines in the bundle, set one (engine, version) as default, upload one part with the default and another part overriding to a different engine+version, and confirm each part records the engine+version it was actually sliced with.

**Acceptance Scenarios**:

1. **Given** two or more available (engine, version) slicers exist, **When** the administrator marks one as the organization default, **Then** parts uploaded without an explicit choice are sliced with that default engine+version.
2. **Given** other engine+version combinations are also available, **When** an operator uploads a part and explicitly selects one, **Then** the part is sliced with the selected engine+version and records it, leaving the default unchanged.
3. **Given** an operator's request names an (engine, version) not present in the current bundle (e.g. a stale client), **When** they submit the upload, **Then** they are told that engine+version is unavailable before processing begins, rather than the job hanging.
4. **Given** parts previously sliced with an older engine+version, **When** the administrator later changes the default, **Then** already-processed parts are unaffected and continue to show the engine+version they were originally sliced with.

---

### User Story 3 - Provision bundled slicers (engine + version) from a manifest at dev/build time (Priority: P2)

As a developer (today) and as the release/packaging process (later), I declare the set of slicers I want — each as an (engine, version) entry — in a simple JSON manifest and run a provided script that fetches the correct per-platform build from each engine's official releases into the application's bundled slicer location, so that a running install ships with those slicers ready to use and no one has to locate, download, and unpack each one by hand for Linux, Windows, or macOS — and the app never downloads anything at runtime.

**Why this priority**: This removes the biggest operational friction and is the "script + json file of versions, downloaded from their GitHub repos, bundled with the shipped app" the user described, now across three engines. It is P2 because Stories 1–2 already deliver a working system for a checkout where the slicers are present; this makes populating them repeatable for development now and shippable for deployment later.

**Independent Test**: On a supported platform, list two entries of different engines in the manifest, run the provisioning script, and confirm both are downloaded from their official sources into the bundled location and become discoverable/usable by the app without hand-typing paths — with the app itself making no network calls.

**Acceptance Scenarios**:

1. **Given** a JSON manifest declaring entries for OrcaSlicer, Bambu Studio, and/or SuperSlicer versions, **When** the developer/build runs the provisioning script for the target platform, **Then** each declared (engine, version) is fetched from that engine's official release source, placed in a predictable per-engine/per-version location inside the bundle, and its integrity is verified before it is considered ready.
2. **Given** a declared (engine, version) is already present in the bundle, **When** the script runs again, **Then** it does not re-download it and reports it as already present (idempotent).
3. **Given** a declared engine/version/platform build does not exist upstream, **When** the script runs, **Then** it fails that entry with a clear message identifying the engine/version/platform and continues with the other entries rather than aborting everything.
4. **Given** slicers were placed in the bundled location, **When** the app starts, **Then** those (engine, version) slicers are discovered and usable automatically, with no per-path configuration and no runtime download.

---

### Edge Cases

- A bundled entry's directory declares one (engine, version) but its binary is actually a different build — since there is no pre-validation, the declared engine+version is what's recorded; correctness of the bundle contents is a provisioning-time (manifest/script) responsibility, not a runtime check.
- Two bundled entries resolve to the same (engine, version) — resolve deterministically and surface the duplication rather than picking arbitrarily.
- A slice is requested while the backend is still discovering the bundle at startup.
- A part/3MF authored for one engine is sliced with a different engine (e.g. an OrcaSlicer project sliced by SuperSlicer) — the engine mismatch may degrade or fail the slice; the recorded engine+version keeps this diagnosable, and any failure surfaces as a prompt slice failure, not a hang.
- One supported engine has no bundled version while others do — that engine simply isn't offered; the others remain fully usable.
- The provisioning script runs on an unsupported or unrecognized platform/architecture (e.g. ARM where only x64 assets exist), or an engine publishes assets for some platforms but not others.
- Loss of network access, a rate-limited or unreachable release source, or a partial/corrupted download during provisioning (dev/build time only).
- Insufficient disk space during provisioning (each engine build is large, and there are now several engines × versions).
- A bundled binary's files are deleted out from under the app after the startup discovery — surfaces as a prompt slice failure when next used, not a hang.
- The host platform of the running app does not match the platform of any bundled slicer.
- A very large number of engine/version combinations is bundled — the picker and default resolution must stay usable.

## Requirements *(mandatory)*

### Functional Requirements

#### Engine & version registry & selection

- **FR-001**: The system MUST support three slicer engines — OrcaSlicer, Bambu Studio, and SuperSlicer — managed uniformly, and MUST build the set of available slicers entirely by the backend auto-scanning the application's bundled slicer directory at deploy/startup. There MUST be no user- or administrator-facing concept of a slicer binary, filesystem path, or system PATH anywhere in the product — the only user-facing concepts are an **engine** and a **version**. Manual path entry MUST NOT exist.
- **FR-002**: The backend MUST determine the available slicers purely by **discovering** which (engine, version) builds are present in the bundled slicer directory at startup — one counts as available if it is present. The system MUST NOT validate slicers by executing them (no runnability probe, no self-report capture) as a pre-flight step, and MUST expose each to users without surfacing any path detail. A binary that turns out to be broken surfaces as a normal slice failure at slice time (FR-006), not a pre-check.
- **FR-003**: The system MUST allow an administrator to designate one available (engine, version) as the organization default used when no explicit choice is made. Until an admin sets a default explicitly, the system MUST auto-default to the newest available version of a preferred engine — preferring OrcaSlicer when present, otherwise the newest version of any available engine — so it is always usable out of the box. "Newest" is determined by comparing the upstream release versions within an engine.
- **FR-004**: The system MUST allow an operator to override, per part add/replace, which available (engine, version) is used to slice that part, defaulting to the organization default when not specified (extending the existing per-part engine-override behavior to also carry the version).
- **FR-005**: The system MUST record the specific engine AND the exact upstream release version actually used (e.g. "SuperSlicer 2.5.59.13" — version string verbatim) on both the part processing job and the resulting part, and MUST display those same values in any picker, so the result is reproducible and auditable. No separate friendly-label scheme is introduced.
- **FR-006**: When a slice is requested against an (engine, version) that is not present in the bundle, OR when the invoked binary fails at slice time, the system MUST fail that request promptly with a message naming the engine and version, and MUST NOT leave a processing job stuck indefinitely.
- **FR-007**: The system MUST prevent selecting-as-default or slicing with an (engine, version) that is not present in the bundle. Because the supported set is append-only (FR-008a), the configured default can never be invalidated by an engine/version being dropped; newly-bundled slicers simply become additionally available.
- **FR-008**: Changing the default MUST NOT retroactively alter parts already processed; each keeps the engine and version it was originally sliced with.
- **FR-008a**: Once an (engine, version) has shipped in any release, it MUST remain bundled in all subsequent releases — the supported set is append-only and nothing is ever dropped. Consequently every engine+version ever recorded on a part MUST remain available for display and re-slicing, and the system need not handle a "recorded slicer no longer present" case.

#### Multi-platform & engine invocation

- **FR-009**: The system MUST support running on Linux, Windows, and macOS hosts, resolving and invoking the correct executable form for the host platform, for each supported engine.
- **FR-009a**: The system MUST invoke each engine with its own correct command form. OrcaSlicer and Bambu Studio share PrusaSlicer-derived CLI conventions; SuperSlicer is a separate PrusaSlicer fork whose CLI differs. The invocation MUST be selected per engine so that a slice against any supported engine produces the same downstream estimate/thumbnail/filament outputs the pipeline already parses.
- **FR-010**: Each discovered slicer MUST carry the engine and the platform it applies to, and the system MUST only slice using an (engine, version) whose platform matches the running host.
- **FR-011**: The system MUST behave sensibly when no discovered slicer matches the host platform (for a given engine or overall), surfacing a clear, actionable message rather than attempting to run an incompatible binary.

#### Provisioning / bundling (dev & build time)

- **FR-012**: The system MUST provide a machine-readable JSON manifest, editable by a developer/maintainer, that declares the desired set of slicers to bundle — each entry an (engine, version) with its target platform, where engine is one of OrcaSlicer / Bambu Studio / SuperSlicer.
- **FR-013**: The system MUST provide a provisioning script that reads the manifest and obtains each declared (engine, version)'s correct build for the target platform from that engine's official GitHub release source.
- **FR-014**: The provisioning script MUST place each slicer in a predictable, per-engine/per-version location inside the application's bundled slicer directory, and MUST verify each download's integrity before marking it ready.
- **FR-015**: The provisioning script MUST be idempotent — already-present (engine, version) builds are not re-downloaded — and MUST report per-entry success/failure without one failure aborting the others.
- **FR-016**: Slicers placed in the bundled location MUST be discovered and used by the app automatically at startup, without a maintainer or operator transcribing each filesystem path.
- **FR-017**: The manifest and provisioning outcome MUST make clear which (engine, version) entries are desired, which are present, and which failed, so a maintainer can reconcile the two.
- **FR-018**: The running application MUST NOT download slicer binaries or contact any release source at runtime; obtaining slicers is exclusively a dev/build-time activity via the provisioning script. The shipped artifact MUST include each supported engine's CLI together with its bundled version binaries.

#### Cross-cutting

- **FR-019**: Setting the organization default version MUST be restricted to an appropriately privileged administrator, and each such change MUST be captured in the activity log. No path/binary management surface exists to gate.
- **FR-020**: The system MUST NOT require or expose any hard-coded slicer path; the set of usable versions comes exclusively from the backend's scan of the bundled slicer directory.
- **FR-021**: Error and status messaging around slicer availability MUST be actionable for a self-hosting operator (which version, which platform, what to do next), consistent with the project's uniform error surface.
- **FR-022**: Multi-version management MUST apply uniformly to all three supported engines — OrcaSlicer, Bambu Studio, and SuperSlicer — via a single engine-generic discovery/selection/recording model (engine is a first-class attribute alongside version). Adding a fourth engine later SHOULD be a matter of teaching the provisioning script its release source and the invocation layer its CLI form, not a redesign. (Supersedes the earlier "OrcaSlicer only" decision — see Clarifications and Assumptions.)

### Key Entities *(include if data involved)*

- **Slicer Engine**: One of the three supported slicers — OrcaSlicer, Bambu Studio, or SuperSlicer. A first-class attribute of every slicer, selection, and record. Each engine has its own upstream GitHub release source and its own CLI invocation form.
- **Available Slicer (Engine + Version)**: A slicer the app can slice with, discovered by the backend scanning the bundled slicer directory at startup — identified by its engine plus exact upstream release version (e.g. "2.2.0") and target platform; it is "available" simply by being present (no runtime validation). The executable location is a backend-internal detail, never surfaced to users. One (engine, version) may be marked the organization default.
- **Version Manifest**: The maintainer-authored JSON declaration of which (engine, version) builds — and platforms — to bundle; the input the dev/build-time provisioning script reconciles against what is already present in the bundle.
- **Part Processing Job** (existing): Extended so the concrete engine AND version used to slice are both captured.
- **Part** (existing): Extended so the engine and version it was produced with are recorded and displayed for reproducibility/audit.
- **Organization** (existing): Extended so its slicing default expresses a default (engine, version) pair, not just an engine.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can make available at least two distinct slicers spanning more than one engine (e.g. an OrcaSlicer version and a SuperSlicer version) and slice different parts with each, with 100% of resulting parts recording the exact engine and version used.
- **SC-002**: 100% of slice requests against a missing or unavailable (engine, version) fail with an actionable message within seconds, and none leave a processing job stuck (zero "stuck processing" occurrences attributable to slicer availability).
- **SC-003**: The same declared manifest provisions the desired (engine, version) builds into the bundle on Linux, Windows, and macOS, and a deployed install runs those bundled slicers with no per-path configuration and no runtime download.
- **SC-004**: Re-running the provisioning script when an (engine, version) is already bundled completes without re-downloading it (idempotent) in the large majority of cases, and a single unavailable/failed entry does not prevent the others from being provisioned.
- **SC-005**: A part sliced with a given engine+version, when re-sliced with that same recorded engine+version, produces an equivalent estimate — i.e. the recorded engine+version is sufficient to reproduce the result.
- **SC-006**: A shipped install can slice a part out of the box using any bundled engine, with no operator install or download step required, and a developer can populate the same slicers locally by running one documented script.
- **SC-007**: Each of the three supported engines, when bundled, can successfully slice a representative part and produce the estimate/thumbnail/filament outputs the pipeline parses — demonstrating the per-engine invocation is correct.

## Assumptions

- The scope is **multi-engine, multi-version slicer management** (FR-022) across OrcaSlicer, Bambu Studio, and SuperSlicer, driven by the user's explicit request; the model is engine-generic so a further engine can be added later without redesign.
- Each supported engine publishes per-platform release builds through its own official GitHub release channel (OrcaSlicer at `SoftFever/OrcaSlicer`, Bambu Studio at `bambulab/BambuStudio`, SuperSlicer at `supermerill/SuperSlicer`), and those builds expose a headless/CLI mode. OrcaSlicer and Bambu Studio use PrusaSlicer-derived CLI conventions; SuperSlicer (also a PrusaSlicer fork) uses a differing CLI — the invocation layer accounts for this per engine (FR-009a).
- **Delivery model (decided with the user):** engines are bundled with the shipped application; the app never downloads binaries at runtime. Downloading is a **dev/build-time** activity performed by the provisioning script — used by developers locally now, and by the release/packaging process to assemble the shipped artifact later. An in-app "download on demand" action is explicitly out of scope.
- The shipped artifact is platform-specific: a build for a given OS bundles that OS's engine build(s); the manifest declares (engine, version) entries and the build fetches the matching platform asset for the artifact being assembled.
- This feature does not change how estimates, thumbnails, or filament requirements are parsed from slicer output — only which engine/version produces that output. Any parsing differences between engines that surface during implementation are handled in the existing parsing layer, not this feature's scope.
- "Multi-platform" means the app and its bundled engines run natively on each host OS; cross-compiling or emulating one platform's slicer on another is out of scope.
- Existing already-processed parts (sliced under the single hard-coded OrcaSlicer path) remain valid; back-filling engine/version onto historical parts is best-effort and not required for this feature.
- The supported (engine, version) set is **append-only**: once one ships it is retained in every later release (the manifest only grows). This is a product/packaging commitment that keeps every recorded slicer reproducible; the tradeoff is monotonically growing artifact size, accepted deliberately — and now larger, since it spans three engines.
- Bundled binaries increase the shipped artifact size substantially; managing that size (which engines/versions to ship, storage/bandwidth) is a packaging decision the manifest expresses, not something the running app manages.
- The default (engine, version) is genuinely **per-organization (per-tenant)** — the application stays multi-tenant by design (tenant-scoped like other org-scoped state), even though the initial target is a single-tenant self-hosted install where only one organization exists in practice. The bundled slicer set itself is install-level (shared by all tenants); only the *default selection* is org-scoped.

## Dependencies

- Builds directly on the existing Phase 4 Part Library slicing pipeline (`ISlicerEngine`, `PartProcessingJob`, organization slicing defaults) and the activity-log post-processor.
- Relies on OpenFarm's existing permission-based RBAC to gate administrator-only registry and provisioning actions.
