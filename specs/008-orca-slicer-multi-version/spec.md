# Feature Specification: OrcaSlicer Multi-Version Management

**Feature Branch**: `008-orca-slicer-multi-version`

**Created**: 2026-07-21

**Status**: Draft

**Input**: User description: "Currently the part feature is implemented but it's not functional yet, because the orca slicer CLI is hard coded and it only support single version. I think we have to re-plan to support multiple versions, multi platform (linux,windows,macos), and might need to have a way to download orca slicers from their github repo. So maybe the software just configure path? Then we have a script to download various versions from github, also maybe we need a json file to determine which version need to download? Please review and plan"

## Context & Problem Statement

Phase 4 (Part Library) shipped in-app slicing, but the slicer integration is not usable in a real deployment:

- The OrcaSlicer/Bambu Studio CLI is referenced by a **single hard-coded filesystem path** per engine (`Slicing:OrcaSlicerCliPath`, default `/usr/local/bin/orca-slicer-cli`). There is **no concept of a slicer version**.
- Exactly **one OrcaSlicer version** is baked into the Linux container image at build time. There is no way to run a second version side by side, and nothing works on Windows or macOS self-hosted installs.
- The **version actually used to slice is not recorded**, so a re-slice or an audit cannot reproduce the original result. This matters because OrcaSlicer's CLI arguments, G-code metadata, and slicing output change between versions, and a 3MF project authored against a particular OrcaSlicer version can slice incorrectly (or fail) under a different one.

**Delivery model (decided):** OrcaSlicer is **bundled with the application** — the shipped artifact includes the OrcaSlicer CLI and the version binaries it supports, so a deployed install can slice out of the box with no runtime download and no operator install step. A **dev/build-time provisioning tool** (a script driven by a JSON version manifest) fetches the desired versions from OrcaSlicer's GitHub releases into the bundle: used by developers locally today, and by the packaging/release process to assemble the shipped artifact later. The **running application never reaches out to GitHub** — it only discovers and uses the versions bundled alongside it (or, in a dev checkout, the versions the script placed).

An operator running a print farm needs to (a) have one or more specific OrcaSlicer versions already available to the app (bundled at ship time, or populated by the dev script during development), (b) choose which version a given part is sliced with, and (c) trust that the recorded version makes re-slicing reproducible. This feature re-plans slicer integration to deliver that.

This specification does not itself re-slice existing parts, redesign the Part Library UI, or change how estimates are parsed — it makes *slicer versions a bundled, selectable, recorded capability* so the Part Library becomes functional.

## Clarifications

### Session 2026-07-21

- Q: Should users ever deal with slicer binaries, filesystem paths, or system PATH? → A: No. Users have no concept of a binary or its location. The backend scans the bundled slicer directory at deploy/startup, populates the set of available versions itself, and the only user-facing concept is a **version** the user picks from. No manual path entry exists anywhere in the product.
- Q: What happens to a part whose recorded slicer version is not bundled in a later deployment? → A: Not applicable by design — once a version is supported it is bundled **forever** (the version set is append-only across releases; a shipped version is never dropped). Therefore a recorded version is always still available, and there is no "version disappeared" case to handle.
- Q: At what granularity does a user choose a slicer version? → A: Both an org-wide default (applied automatically, admin-set) and an optional per-part override at upload/replace time. Most uploads take the default with no decision; the override exists for parts authored against a different version.
- Q: What identifies a "version" to the user? → A: The exact OrcaSlicer release version as published upstream (e.g. "2.2.0"), displayed in the picker and recorded on the part verbatim — no separate friendly-label scheme.
- Q: When/how does the backend validate a bundled version? → A: It does not validate. The backend simply **discovers** the versions present in the bundle at startup — a version's availability means it is present. No executing binaries to verify runnability, no self-report probing. A genuinely broken binary surfaces as a normal, prompt slice failure at slice time (existing slicer-failure handling), not a pre-flight check.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Slice with a bundled, backend-discovered OrcaSlicer version (Priority: P1)

As an operator, I upload a part and it is sliced with a known OrcaSlicer version that the backend already discovered from the bundle — without me knowing or touching any binary or path — so that slicing works predictably instead of failing.

**Why this priority**: This is the minimum that makes the Part Library functional at all — without at least one discovered, validated slicer version, no part that requires slicing can ever be processed. Everything else builds on this.

**Independent Test**: With an OrcaSlicer version present in the bundle, upload a part that requires slicing and confirm it slices successfully and the resulting part records the version used. With the bundled binary absent/broken, confirm the upload fails fast with a clear message instead of hanging.

**Acceptance Scenarios**:

1. **Given** the backend has discovered the bundle at startup, **When** an OrcaSlicer version is present, **Then** that version is shown to users as "available" with its exact release version and the platform it applies to — and no path is shown anywhere.
2. **Given** exactly one available OrcaSlicer version exists, **When** an operator uploads a part that requires slicing, **Then** the part is sliced with that version and the version identifier is stored on the resulting part and its processing job.
3. **Given** a bundled version whose binary turns out to be broken (not caught up-front, since there is no pre-validation), **When** an operator uploads a part that uses it, **Then** the slice attempt fails promptly at slice time with a clear message, and no job is left stuck in a processing state.
4. **Given** no OrcaSlicer version is present in the bundle, **When** an operator uploads a part that requires slicing, **Then** the app rejects/queues the request with a clear "no slicer available" explanation rather than silently accepting it.

---

### User Story 2 - Run multiple OrcaSlicer versions side by side and choose one (Priority: P1)

As a farm administrator, I have more than one OrcaSlicer version available (all discovered from the bundle) and set a default, and an operator can override which version a particular part is sliced with, so that parts authored against different OrcaSlicer versions each slice correctly.

**Why this priority**: The single-version limitation is the core defect the user called out. Being able to hold several versions and pick per-part is what makes the feature "multi-version" rather than just "configurable path". It reuses the existing per-part engine-override pattern, so it is a natural P1 companion to Story 1.

**Independent Test**: With two OrcaSlicer versions in the bundle, set one as default, upload one part with the default and another part overriding to the second version, and confirm each part records the version it was actually sliced with.

**Acceptance Scenarios**:

1. **Given** two available OrcaSlicer versions exist, **When** the administrator marks one as the organization default, **Then** parts uploaded without an explicit version choice are sliced with that default.
2. **Given** a non-default version is also available, **When** an operator uploads a part and explicitly selects that version, **Then** the part is sliced with the selected version and records it, leaving the default unchanged.
3. **Given** an operator's request names a version not present in the current bundle (e.g. a stale client), **When** they submit the upload, **Then** they are told that version is unavailable before processing begins, rather than the job hanging.
4. **Given** parts previously sliced with an older version, **When** the administrator later changes the default version, **Then** already-processed parts are unaffected and continue to show the version they were originally sliced with.

---

### User Story 3 - Provision bundled OrcaSlicer versions from a manifest at dev/build time (Priority: P2)

As a developer (today) and as the release/packaging process (later), I declare the set of OrcaSlicer versions I want in a simple JSON manifest and run a provided script that fetches the correct per-platform build from OrcaSlicer's official releases into the application's bundled slicer location, so that a running install ships with those versions ready to slice and no one has to locate, download, and unpack each version by hand for Linux, Windows, or macOS — and the app never downloads anything at runtime.

**Why this priority**: This removes the biggest operational friction and is the "script + json file of versions, downloaded from their GitHub repo, bundled with the shipped app" the user described. It is P2 because Stories 1–2 already deliver a working system for a checkout where the versions are present; this makes populating them repeatable for development now and shippable for deployment later.

**Independent Test**: On a supported platform, list two desired versions in the manifest, run the provisioning script, and confirm both versions are downloaded from the official source into the bundled location, verified, and become discoverable/usable by the app without hand-typing paths — with the app itself making no network calls.

**Acceptance Scenarios**:

1. **Given** a JSON manifest declaring two OrcaSlicer versions, **When** the developer/build runs the provisioning script for the target platform, **Then** each declared version is fetched from OrcaSlicer's official release source, placed in a predictable per-version location inside the bundle, and its integrity/runnability is verified before it is considered ready.
2. **Given** a declared version is already present in the bundle, **When** the script runs again, **Then** it does not re-download that version and reports it as already present (idempotent).
3. **Given** a declared version or platform build does not exist upstream, **When** the script runs, **Then** it fails that entry with a clear message identifying the version/platform and continues with the other entries rather than aborting everything.
4. **Given** versions were placed in the bundled location, **When** the app starts, **Then** those versions are discovered and usable automatically, with no per-path configuration and no runtime download.

---

### Edge Cases

- A bundled version's directory declares one version but its binary is actually a different build — since there is no pre-validation, the declared version is what's recorded; correctness of the bundle contents is a provisioning-time (manifest/script) responsibility, not a runtime check.
- Two bundled entries resolve to the same version — resolve deterministically and surface the duplication rather than picking arbitrarily.
- A slice is requested for a version while the backend is still discovering the bundle at startup.
- The provisioning script runs on an unsupported or unrecognized platform/architecture (e.g. ARM where only x64 assets exist).
- Loss of network access, a rate-limited or unreachable release source, or a partial/corrupted download during provisioning (dev/build time only).
- Insufficient disk space for one or more versions during provisioning (each OrcaSlicer build is large).
- A bundled version's files are deleted out from under the app after the startup discovery — surfaces as a prompt slice failure when next used, not a hang.
- The host platform of the running app does not match the platform of any bundled version.
- A very large number of versions is bundled — the version picker and validation must stay usable.

## Requirements *(mandatory)*

### Functional Requirements

#### Version registry & selection

- **FR-001**: The system MUST build the set of available OrcaSlicer versions entirely by the backend auto-scanning the application's bundled slicer directory at deploy/startup. There MUST be no user- or administrator-facing concept of a slicer binary, filesystem path, or system PATH anywhere in the product — the only user-facing concept is a **version**. Manual path entry MUST NOT exist.
- **FR-002**: The backend MUST determine the available versions purely by **discovering** what is present in the bundled slicer directory at startup — a version counts as available if it is present. The system MUST NOT validate versions by executing them (no runnability probe, no self-report capture) as a pre-flight step, and MUST expose each version to users without surfacing any path detail. A binary that turns out to be broken surfaces as a normal slice failure at slice time (FR-006), not a pre-check.
- **FR-003**: The system MUST allow an administrator to designate one available version as the organization default used when no explicit version is chosen. When only one version is available, it MUST be usable as the default without further configuration.
- **FR-004**: The system MUST allow an operator to override, per part add/replace, which available version is used to slice that part, defaulting to the organization default when not specified (mirroring the existing per-part engine-override behavior).
- **FR-005**: The system MUST record the specific slicer version actually used — the exact upstream OrcaSlicer release version (e.g. "2.2.0"), verbatim — on both the part processing job and the resulting part, and MUST display that same value in any version picker, so the result is reproducible and auditable. No separate friendly-label scheme is introduced.
- **FR-006**: When a slice is requested against a version that is not present in the bundle, OR when the invoked binary fails at slice time, the system MUST fail that request promptly with a message naming the version, and MUST NOT leave a processing job stuck indefinitely.
- **FR-007**: The system MUST prevent selecting-as-default or slicing with a version that is not present in the bundle. Because the version set is append-only (FR-008a), the configured default can never be invalidated by a version being dropped; newly-bundled versions simply become additionally available.
- **FR-008**: Changing the default MUST NOT retroactively alter parts already processed; each keeps the version it was originally sliced with.
- **FR-008a**: Once a version has shipped in any release, it MUST remain bundled in all subsequent releases — the supported version set is append-only and a version is never dropped. Consequently every version ever recorded on a part MUST remain available for display and re-slicing, and the system need not handle a "recorded version no longer present" case.

#### Multi-platform

- **FR-009**: The system MUST support running on Linux, Windows, and macOS hosts, resolving and invoking the correct OrcaSlicer executable form for the host platform.
- **FR-010**: Each discovered version MUST carry the platform it applies to, and the system MUST only slice using a version whose platform matches the running host.
- **FR-011**: The system MUST behave sensibly when no discovered version matches the host platform, surfacing a clear, actionable message rather than attempting to run an incompatible binary.

#### Provisioning / bundling (dev & build time)

- **FR-012**: The system MUST provide a machine-readable JSON manifest, editable by a developer/maintainer, that declares the desired set of OrcaSlicer versions to bundle.
- **FR-013**: The system MUST provide a provisioning script that reads the manifest and obtains each declared version's correct build for the target platform from OrcaSlicer's official GitHub release source.
- **FR-014**: The provisioning script MUST place each version in a predictable, per-version location inside the application's bundled slicer directory, and MUST verify each download's integrity and runnability before marking it ready.
- **FR-015**: The provisioning script MUST be idempotent — already-present versions are not re-downloaded — and MUST report per-version success/failure without one failure aborting the others.
- **FR-016**: Versions placed in the bundled location MUST be discovered and used by the app automatically at startup, without a maintainer or operator transcribing each filesystem path.
- **FR-017**: The manifest and provisioning outcome MUST make clear which versions are desired, which are present, and which failed, so a maintainer can reconcile the two.
- **FR-018**: The running application MUST NOT download slicer binaries or contact the release source at runtime; obtaining versions is exclusively a dev/build-time activity via the provisioning script. The shipped artifact MUST include the OrcaSlicer CLI together with its bundled version binaries.

#### Cross-cutting

- **FR-019**: Setting the organization default version MUST be restricted to an appropriately privileged administrator, and each such change MUST be captured in the activity log. No path/binary management surface exists to gate.
- **FR-020**: The system MUST NOT require or expose any hard-coded slicer path; the set of usable versions comes exclusively from the backend's scan of the bundled slicer directory.
- **FR-021**: Error and status messaging around slicer availability MUST be actionable for a self-hosting operator (which version, which platform, what to do next), consistent with the project's uniform error surface.
- **FR-022**: Multi-version management MUST apply to OrcaSlicer for this feature. Bambu Studio remains a single-configured-path engine, but the version registry MUST be designed engine-generically so Bambu Studio can be brought under the same model later without a redesign. (Resolved decision — see Assumptions.)

### Key Entities *(include if data involved)*

- **Available Slicer Version**: A version the app can slice with, discovered by the backend scanning the bundled slicer directory at startup — identified by its exact upstream OrcaSlicer release version (e.g. "2.2.0") plus target platform; it is "available" simply by being present (no runtime validation). The executable location is a backend-internal detail, never surfaced to users. One version may be marked the organization default.
- **Version Manifest**: The maintainer-authored JSON declaration of which OrcaSlicer versions (and platforms) to bundle; the input the dev/build-time provisioning script reconciles against what is already present in the bundle.
- **Part Processing Job** (existing): Extended so the concrete slicer version used to slice is captured alongside the already-recorded engine.
- **Part** (existing): Extended so the slicer version it was produced with is recorded and displayed for reproducibility/audit.
- **Organization** (existing): Extended so its slicing default expresses not just an engine but the default slicer version.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can make at least two distinct OrcaSlicer versions available and slice different parts with each, with 100% of resulting parts recording the exact version used.
- **SC-002**: 100% of slice requests against a missing or unavailable version fail with an actionable message within seconds, and none leave a processing job stuck (zero "stuck processing" occurrences attributable to slicer availability).
- **SC-003**: The same declared manifest provisions the desired versions into the bundle on Linux, Windows, and macOS, and a deployed install runs those bundled versions with no per-path configuration and no runtime download.
- **SC-004**: Re-running the provisioning script when a version is already bundled completes without re-downloading it (idempotent) in the large majority of cases, and a single unavailable/failed version does not prevent the others from being provisioned.
- **SC-005**: A part sliced with a given version, when re-sliced with that same recorded version, produces an equivalent estimate — i.e. the recorded version is sufficient to reproduce the result.
- **SC-006**: A shipped install can slice a part out of the box using its bundled OrcaSlicer, with no operator install or download step required, and a developer can populate the same versions locally by running one documented script.

## Assumptions

- The scope is **OrcaSlicer version management** (FR-022), driven by the user's explicit request; Bambu Studio stays a single-configured-path engine, with the registry designed engine-generically so it can be added later.
- **Delivery model (decided with the user):** OrcaSlicer is bundled with the shipped application; the app never downloads binaries at runtime. Downloading is a **dev/build-time** activity performed by the provisioning script — used by developers locally now, and by the release/packaging process to assemble the shipped artifact later. An in-app "download on demand" action is explicitly out of scope.
- OrcaSlicer continues to publish per-platform release builds through its official GitHub release channel, and those builds expose a headless/CLI mode compatible with the existing slice invocation.
- The shipped artifact is platform-specific: a build for a given OS bundles that OS's OrcaSlicer build(s); the manifest declares versions and the build fetches the matching platform asset for the artifact being assembled.
- This feature does not change how estimates, thumbnails, or filament requirements are parsed from slicer output — only which slicer binary/version produces that output.
- "Multi-platform" means the app and its bundled slicer versions run natively on each host OS; cross-compiling or emulating one platform's slicer on another is out of scope.
- Existing already-processed parts (sliced under the single hard-coded version) remain valid; back-filling a version label onto historical parts is best-effort and not required for this feature.
- The supported version set is **append-only**: once a version ships it is retained in every later release (the manifest only grows). This is a product/packaging commitment that keeps every recorded version reproducible; the tradeoff is monotonically growing artifact size, accepted deliberately.
- Bundled binaries increase the shipped artifact size substantially; managing that size (which versions to ship, storage/bandwidth) is a packaging decision the manifest expresses, not something the running app manages.
- The organization remains single-tenant per self-hosted install (per project foundation), so "organization default version" is effectively the install-wide default.

## Dependencies

- Builds directly on the existing Phase 4 Part Library slicing pipeline (`ISlicerEngine`, `PartProcessingJob`, organization slicing defaults) and the activity-log post-processor.
- Relies on OpenFarm's existing permission-based RBAC to gate administrator-only registry and provisioning actions.
