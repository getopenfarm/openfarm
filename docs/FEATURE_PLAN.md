# OpenFarm — Feature Plan

## Overview

OpenFarm is a print farm management webapp covering printers, filament, maintenance, parts, assemblies (BOMs), and print jobs. It's being built open-source-first, kept private for now, and architected to support multi-tenancy from day one in case of future monetization.

## Guiding principles

- **MVP-first, not data-model-first.** Ship a real, demoable capability as early as possible and let later phases extend it, rather than building the full schema before anything is usable. The previous version of this plan required filament tracking, printer profiles, and sliced files to all exist before a printer could even be added to the fleet — this version inverts that: fleet management and live Bambu Lab connectivity are the *first* thing built, because they're the most visible, most demoable capability and the Bambu local-MQTT integration path is well-understood, not R&D-risk territory (see Phase 1).
- **Ordered by day-to-day print-farm operational value, not just build convenience.** After Phase 1 (which is both a structural prerequisite for almost everything else *and* the single most-checked piece of information in a real farm), phases are sequenced by what an actual farm operator needs next in practice. Phase 2 (Profiles) is the one deliberate exception to "operational value first" — it's foundational plumbing rather than a visible day-to-day capability, pulled forward because a concrete acquisition path (slicer-bundle import) exists for it now, and because real sliced files and jobs later need real profiles under them rather than a throwaway stand-in. From Phase 3 onward the ordering resumes prioritizing operational and product value — not running out of filament mid-job, then a real parts catalog — ahead of internal org/permission hygiene (Phase 5) that doesn't affect daily operation. See Phases 2–5 below and the Dependency notes section for the reasoning.
- Usable by a solo operator or small farm without multi-tenant/SaaS complexity getting in the way.
- Automation is *suggested*, not forced — a human still physically loads spools and parts, so the system should assist decisions rather than take them over.
- Every feature should work for a self-hosted single-farm install and a future hosted multi-tenant version without redesign.
- TDD is applied selectively, not uniformly: deterministic domain logic (state machines, BOM decomposition, filament weight math) gets red-green-refactor; orchestration-only application code, EF Core plumbing, the real Bambu MQTT client, and React components are test-after or fake/fixture-based instead. See Phase 1's sub-milestones for a concrete example of where this split falls.

## Phase groupings (for orientation only)

The structure below is intentionally flat — every phase is its own top-level section so any one of them can get a full detail/dev plan without dragging a dozen unrelated features along with it. These groupings are just informal labels for orientation, not a re-introduction of nested phases:

- **Foundation:** Phase 0
- **Core MVP:** Phases 1–10
- **Smart features & integrations:** Phases 11–17
- **Scale & monetization:** Phases 18–23

## Dependency notes

**Core MVP (1–10) is ordered by a combination of demo-ability and real print-farm operational importance, not data-model completeness or internal build convenience:**
- **Phase 1** (Fleet management + live Bambu integration, including a live camera feed) is built first and deliberately ships *without* linking a profile record onto the printer entity — a printer's spec fields stay inlined directly on the printer entity. Phase 2 introduces a standalone machine-profile catalog (via slicer-bundle import) without wiring it onto `Printer` rows; that linkage — not the catalog's existence — is what stays deferred, until whichever later phase actually consumes it (Phase 4 — Part library, which folds in sliced-file records). Phase 1 is also the single structural prerequisite almost everything else in this plan depends on — every other phase either attaches to a printer row or reads its status history.
- **Phase 2** (Profiles — machine + filament, imported from slicer bundles) replaces the former minimal-print-flow phase outright rather than deferring it: a sliced file only means something once it's tied to a real machine/filament profile, so the next real print-trigger demo moment is the full Jobs phase (Phase 7) once parts/assemblies/sliced files exist under it, not an intermediate ad-hoc file push. Building Profiles this early also pulls forward what the old plan slated for Phase 4 (printer-profile extraction) and the whole of the old Phase 5 (Filament profiles) — justified now by slicer-bundle import being a concrete, immediate acquisition path for real profile data, not by duplication across multiple owned printers (the old Phase 4 trigger). Phase 5 keeps only tags/feed-config/RBAC now that profile extraction has moved here.
- **Phase 3** (Filament tracking) is ranked ahead of fleet hardening/RBAC for the same operational-value reason: a spool running out mid-print is the most common real-world failure a farm operator deals with day to day, and basic spool tracking (add/track/mark-finished) needs zero printer knowledge at all. The one exception is called out explicitly in Phase 3 itself: its *auto-reload* branch needs to know which feed slots are configured as auto-reload, and that field doesn't exist until Phase 5 — core filament tracking isn't blocked on Phase 5, only that one behavior is.
- **Phase 4** (Part library, with sliced files and in-app slicing folded in) is pulled forward ahead of fleet hardening/RBAC because it's real, user-visible product catalog work — parts an operator actually adds, browses, and manages the files of — rather than internal org/multi-user hygiene, and it needs zero printer knowledge (see the Phases 4 and 6 note below), so nothing in Phase 1's live-connector work gates it. Sliced-file formalization (originally its own phase) is folded in here rather than deferred, and this phase now also folds in the old plan's separate Phase 23 (In-app slicing) for the same reason: a part isn't demoable/job-ready until its nozzle diameter and material type are known, and automatic in-app slicing on upload derives that immediately rather than requiring an external manual slicer round-trip or a separate later phase — there's no reason to make either wait behind Fleet hardening (Phase 5) or Assembly (Phase 6). This primary compatibility match (nozzle diameter, generic material type — see the phase's own detail below) is either parsed straight from an already-sliced upload or derived from an in-app slice, needing no dependency on Phase 2 at all; only the optional narrower Filament Profile/color-variant override does.
- **Phase 5** (Fleet hardening + real RBAC) — tags, feed-config, and the real permission catalog — moves here specifically *because* it's org/multi-user hygiene rather than something a solo/small-farm operator needs to see working day to day, and now sits behind Part library for the same product-over-hygiene reason. It's real, necessary work, just not more urgent than Phases 2–4.
- **Phases 4 and 6** (Part library — including its folded-in sliced-file records and in-app slicing — and Assembly/BOM) need zero printer knowledge — no API, no file format, no live connection — beyond what Phase 2 optionally provides for a part's narrower Filament Profile/color-variant override, and can be built in parallel with or independently of Phase 5 (Fleet hardening + RBAC), which touches printers directly but doesn't block them.
- **Phases 7–10** (Jobs, Maintenance, Dashboard, Backup & restore) tie everything above together and stay last, same as the original plan.

**Filament spools still do not depend on a filament profile** (Phase 3 vs. Phase 2) — a spool can be added and tracked through in storage / using / finished with no profile attached; a profile is only required once the spool is actually used in a job.

**Smart features and scale phases carry the same dependencies they always did:**
- Phases 11–17 mostly depend only on their Phase 1–10 prerequisites (e.g. Phase 11 needs Phase 1's idle state and Phase 7's queue; Phase 17 needs the data capture that starts in Phase 1 and Phase 7).
- Phases 18–23 build on the swappable printer-connector boundary from Phase 1 and the multi-org groundwork from Phase 0.

## Phase 0 — Foundation (prerequisite, not user-facing)

**Status: Delivered.** Implemented via `specs/001-foundation/` (spec, plan, tasks) — real JWT
login for the seeded operator, auto-created single organization, encrypted/masked/immutable
connection secrets, the structural activity log, and the base shadcn UI kit are all live.
Phase 1 can now build on top of it.

Scoped down deliberately for the MVP-first pivot — each piece below is cut independently based on whether stubbing it is defensible or a corner that bites us later, not lumped into one all-or-nothing "foundation" block:

- **Auth: real, but coarse.** One seeded user, real JWT-based login, no self-registration, no role-management UI. A no-auth throwaway slice was considered and rejected — this system stores real Bambu printer access codes and the activity log needs a real actor, so retrofitting auth later would mean unwinding both.
- **Multi-org: schema now, resolution machinery later.** `organization_id` is present on every tenant-scoped table from day one — adding it retroactively would mean a migration touching every existing row plus a real risk window of a forgotten tenant filter. What's deferred is the actual dynamic claim-based resolution (Finbuckle.MultiTenant): for a single auto-created organization, a static single-tenant resolver is enough until Phase 18 needs real org-switching.
- **RBAC: placeholder policy shim now, real catalog in Phase 5.** A thin permission-shaped policy attribute is added to endpoints from the start (so it doesn't need retrofitting later), but its handler just checks "authenticated" until Phase 5 swaps in the real seeded permission catalog and four roles (Owner/Admin/Operator/Viewer, Operator/Viewer optionally scoped to printer tags — see Phase 5).
- **Connection secrets: real encryption from day one, not stubbed.** These rows hold a real physical printer's LAN access code — a working credential, not a demo fixture. Encrypted at rest using ASP.NET Core's `IDataProtector` (no external KMS needed for self-host) from the first commit; moving to a proper secrets-manager/KMS-backed per-tenant key is deferred to Phase 18 (hosted SaaS), not before.
- **Activity log: built for real.** A generic, polymorphic activity log (entity, event type, actor, timestamp) underlies every status change from the start — this is what Phase 1's printer status-change logging and Phase 17's analytics both read from, and it needs to start accumulating from day one rather than being bolted on retroactively.

## Phase 1 — Fleet management + live Bambu integration

**The first feature built after the scaffold.** Combines what the previous plan split across "Fleet management," "Live printer integration," and "Camera / live monitoring" into one phase, because the whole point of building fleet management first is to make it live and visible from the start rather than a static CRUD screen that gets live status (and, later, video) bolted on much later.

- Printer entity with spec fields **inlined directly** (name, model, IP address, serial number, connection-secret reference) — no separate printer-profiles table yet, and no link onto one either (see Dependency notes above; Phase 2 builds a standalone machine-profile catalog, not yet wired onto this entity).
- Printer status modeled as an explicit domain state machine: `Idle` / `Printing` / `AwaitingClear`, split into two operations — a strict `TransitionTo` for human-initiated commands (e.g. "Mark ready") that throws on an illegal manual jump, and a lenient `ObserveStatus` for connector-reported physical reality, which reconciles and logs rather than rejecting a fact the printer is already doing (e.g. someone started a print from the Bambu app directly). `Offline`/connectivity is tracked separately as connector connection state, not as a `PrinterStatus` member. `Maintenance` is deferred until Phase 8 exists to enter it.
- `IPrinterConnector` interface, with a `FakePrinterConnector` so application code and UI can be built and tested well before the real connector is finished.
- A real `BambuLanConnector` over MQTTnet, connecting to Bambu X1/P1/A1-series printers in LAN-only + Developer Mode: MQTT over TLS on port 8883 (topic `device/<serial>/report` for status/telemetry/AMS-slot pushes), validated against a physical printer.
- SignalR push (`/hubs/farm`) into a minimal fleet-list + printer-detail UI — status/telemetry updates live without a page refresh.
- A minimal live camera view on the printer-detail page (RTSP-ish stream on port 322 for X1/H2/P2, port 6000 for P1/A1) — folded in here rather than left until the old Phase 26, since it reuses the exact same LAN connection/connector boundary and adds a much more visceral "it's really working" demo moment for close to no extra cost. Recording/timelapse linkage stays a later phase (see Phase 23).
- **Scope for this phase's demo bar: live monitoring only** (status, telemetry, AMS slots, camera feed). No print-trigger or file push yet — that's Phase 7 (Jobs). The goal here is "watch the fleet update live off a real printer," not "make it do something."
- Every status change and connector reconciliation is logged via the Phase 0 activity log.

### Sub-milestones for this phase

Sequencing logic: a small throwaway hardware spike first (de-risks the one genuinely uncertain element — the real Bambu MQTT protocol — before committing to interface shape), then a thin vertical slice (visible CRUD before the real connector, real connector before real-time push) rather than a full backend-then-UI build-out.

- **M0.0 — Add reverse-engineered Bambu API reference as a git submodule.** The user has already gathered multiple sources of reverse-engineered Bambu Lab API documentation/code (protocol details, message shapes, MQTT/FTP/camera behavior across models) from prior investigation, published at [tanghq33/bambulab-api-docs](https://github.com/tanghq33/bambulab-api-docs) (`develop` branch). This is added as a git submodule at `third_party/printers/bambulab/` and used as the primary protocol reference — supersedes/supplements the public community projects noted below. `third_party/printers/<brand>/` is the standing convention for this kind of reference material, so a future brand (see Phase 14 — Expanded live printer integration) gets its own sibling directory without touching this one.
  For context, public prior art confirming this integration path is well-trodden (not R&D-risk territory): [Bambu.NET](https://github.com/ColdThunder11/Bambu.NET) (MQTTnet-based `BambuMQTTClient`, closest match to this project's C#/.NET stack), [BambuSharp](https://libraries.io/nuget/BambuSharp) (NuGet, wraps temps/progress/AMS state), and [bambulabs_api](https://pypi.org/project/bambulabs-api/) (Python, documents MQTT/FTP/camera behavior in detail even though it's a different language).
- **M0.1 — Bambu MQTT spike (throwaway, no product code).** Raw MQTTnet client against the physical printer in LAN-only + Developer Mode: TLS connect on 8883, subscribe `device/<serial>/report`, dump raw JSON. Cross-reference against the M0.0 submodule's documented message shapes. Capture real fixture payloads for M0.5's tests.
- **M0.2 — Minimal-but-real plumbing.** Seeded user + JWT login, `Organization` singleton + static single-tenant resolver, `connection_secrets` table with real `IDataProtector` encryption, `activity_log` table + FastEndpoints post-processor proven against one dummy mutation.
- **M0.3 — Printer domain + CRUD (no live connector yet).** `Printer` entity, `PrinterStatus` state machine — **TDD**: `TransitionTo` + `ObserveStatus`, all transition pairs, invalid-transition exceptions, reconciliation/logging behavior on a physical-reality mismatch. FluentValidation, FastEndpoints CRUD endpoints, EF Core migration.
- **M0.4 — Minimal UI.** Fleet list + add-printer form (react-hook-form + zod) + detail/status view, wired to the real Orval-generated client. Plain TanStack Query polling — no SignalR yet.
- **M0.5 — Real `BambuLanConnector`.** Built from M0.1's fixtures behind `IPrinterConnector`, developed against `FakePrinterConnector` first. **Test-after / fake-based**: fixture-replay unit tests (real captured JSON → parsed telemetry/AMS-slot model), in-process MQTT broker tests for subscribe/publish wiring. **Manual verification only, not in CI:** final validation against the physical printer — TLS handshake, reconnect-after-drop, AMS accuracy.
- **M0.6 — SignalR live wiring.** `/hubs/farm` pushes status/telemetry, frontend subscribes into the TanStack Query cache — the actual demo moment: the fleet page updates live off a real printer.
- **M0.7 — Minimal live camera view.** Render the printer's local video stream (port 322 or 6000, per model) on the printer-detail page — no recording/timelapse yet (see Phase 23).

### Risks/notes

- Enabling LAN-only + Developer Mode disconnects the printer from Bambu Cloud/the official app while active — a real workflow disruption during development, not just a footnote, since it's the user's own physical printer.
- Local MQTT access is Bambu-permitted but unofficial/unversioned — firmware updates have previously altered third-party access in the past. This is an ongoing maintenance risk for `BambuLanConnector`, not a one-time cost.
- Camera/live-video port differs by printer generation (322 for X1/H2/P2, 6000 for P1/A1); FTPS file transfer runs on port 990 and becomes relevant in Phase 7 — Jobs, not this phase.

## Phase 2 — Profiles (machine + filament, imported from slicer bundles)

- Two standalone catalogs: **machine profiles** (nozzle size, bed volume, enclosure, supported materials, whether the model supports live integration — the fields originally slated for the old plan's Phase 4 `printer_profiles` extraction) and **filament profiles** (material, brand, color, tuned settings; schema kept close to Spoolman's shape, consistent with Phase 3's filament modeling — see Phase 13 — Spoolman-compatible filament API).
- Import flow: the user uploads a slicer bundle — an OrcaSlicer profile-bundle `.zip` (individual printer/filament preset JSON files) or a Bambu Studio project/preset export (a `.3mf`, itself a zip container, or Bambu's own preset bundle). The backend extracts the bundle and parses whichever machine and filament presets it finds, then presents them to the user as a **selection list** rather than importing everything blindly — a bundle commonly contains multiple presets (e.g. several filament colors), and the user picks which ones to keep.
- Base/override preset relationships present in the source bundle (OrcaSlicer/Bambu Studio's own inheritance model — e.g. a color-specific filament preset overriding a shared base) are preserved as a parent/child relationship in OpenFarm's schema, rather than flattened into independent copies.
- Imported profiles are a standalone library in this phase — not yet linked to a specific fleet `Printer` row (Phase 1) or consumed by a job. That linkage happens naturally once Phase 4 (Part library, which folds in sliced-file records) and Phase 7 (Jobs) need to reference a specific machine/filament profile.
- Structured per-field manual create/edit forms are explicitly out of scope for this phase's MVP — profiles exist only via import. Once imported, a profile's raw data can be viewed and overridden through a **JSON editor/viewer** rather than dedicated form fields; purpose-built forms per profile attribute are a deliberately deferred follow-on once real imported data has settled the schema.
- No live printer connection needed — pure file-parsing + catalog CRUD, the same "zero printer knowledge required" property Phase 4 (Part library) has.

## Phase 3 — Filament tracking

- Ranked ahead of fleet hardening/RBAC (Phase 5) for the same operational-value reason as Phase 2: a spool running out mid-print is the single most common daily operational failure on a real print farm, and the core lifecycle below needs zero printer knowledge. **One explicit exception:** the auto-reload branch needs to know which feed slot is configured as auto-reload, and that configuration field doesn't exist until Phase 5 — everything else in this phase works today, only that one behavior activates once Phase 5 lands.
- Spool tracking never requires a physical barcode/unique ID — an optional identifier can be added later for anyone who wants to scan spools, but it's never required to use the feature (see Phase 12 — Optional spool identification).
- Each spool has three states: **in storage** (not loaded), **using** (loaded onto a feed slot, actively tracked), and **finished** (depleted). Remaining weight is visible at the moment a spool is removed, so the operator can decide immediately whether it goes back to storage (still has usable filament) or gets marked finished — no in-between holding state needed.
- Remaining weight is estimated automatically from job usage while a spool is in use.
- For single-spool and AMS slots: marking a spool finished stays a manual action. A slot running out doesn't auto-continue, so the operator is already there physically replacing it — and pulling a spool doesn't always mean it's empty (could be a deliberate color change).
- For auto-reload slots (see the exception noted above, once Phase 5's feed-config field exists): running out is exactly the moment the device is built to react to, so the system can detect it directly from the numbers — once cumulative job usage catches up to a spool's starting weight, mark it finished and flip the active slot to the standby spool automatically, without waiting on a manual mark. This is still an estimate rather than a precise measurement, so a manual override should stay available in case actual usage drifts from it over time.
- Before flipping to the standby spool, the system checks that its assigned filament profile matches the job's required material and color. A mismatch doesn't pause the swap — the entire point of auto-reload is continuing unattended — but it raises a dashboard warning and, once notifications exist (see Phase 16 — Notifications), an alert, and the mismatch is logged against the job so it's traceable afterward rather than silent.
- Internally modeled with a schema deliberately close to Spoolman's (spool / filament / vendor), even though this data lives natively in OpenFarm rather than depending on a separate Spoolman instance — see Phase 13 — Spoolman-compatible filament API for why.
- **No printer knowledge required** for the core lifecycle. Everything above (except the noted auto-reload exception) works identically regardless of which printer brand or model eventually loads the spool.

## Phase 4 — Part library (with sliced files and in-app slicing)

- Add parts with a name, an *optional* Filament when the file needs slicing (a generic material like "Any PETG," a specific filament variant, or nothing at all — see the "any material" note below), and **exactly one file** (STL, 3MF, or G-code) — a part never holds more than one file at a time; Assembly (Phase 6) is the mechanism for combining multiple distinct parts, so a Part itself stays deliberately atomic. A part is only ever saved once fully described — **there is no manual-entry fallback anywhere**: not for nozzle diameter, material type, print-time/weight estimate, or thumbnail. If a file can't be parsed or sliced, or no printer with a known nozzle diameter is registered yet, the add/replace is rejected outright rather than saved incomplete. **Filament-diameter matching is dropped entirely** — the desktop-printer market this project targets is virtually uniform at 1.75mm, and printer telemetry doesn't expose it anyway.
- **A requires-slicing file added with no Filament specified defaults to "any material," not a rejection.** The system still runs its own immediate in-app slice — needed regardless, since a part is never saved incomplete — against a fixed, internal, non-operator-configurable generic default material used solely to produce that immediate estimate/nozzle-diameter/thumbnail; the saved part's filament requirement is then recorded as fully unconstrained (matches any spool of any material), broader than "any PETG" (which still requires PETG specifically). This is a deliberate, named accuracy tradeoff — the estimate is a best-effort approximation, not guaranteed accurate for whatever material the part is eventually printed in once matched to a real job (Phase 7) — traded off against the friction of forcing every STL upload to commit to a material before the operator necessarily knows or cares which one it'll end up using. The operator can always avoid this path by specifying a real Filament up front, or narrow an "any material" part to a specific Filament Profile/color-variant later at the same review/confirm step every other requirement uses. This does not extend to an already-sliced file — its process parameters are physically fixed to one material once sliced, so "any material" is never valid there.
- There is no separate "source model" concept — a part's one file is simply determined, at upload, to either **require slicing** (an STL, or a 3MF without embedded sliced G-code) or **not require slicing** (a G-code file, or a 3MF that already contains sliced G-code/toolpath data). Sliced-file formalization is folded into this phase rather than deferred to a later one, and this phase now also folds in the old plan's separate Phase 23 (In-app slicing) for the same reason: a file requiring slicing carries none of the parseable time/weight/nozzle metadata an already-sliced file does — only an actual slicing pass produces it.
- **Automatic in-app slicing never persists a sliced-output file, and a successful slice is reviewed before it's saved.** Uploading a file that requires slicing automatically runs it through a headless slicer engine (OrcaSlicer by default; Bambu Studio also supported; configurable at the organization level — the *only* organization-level slicing setting) against the specified Filament and **whichever nozzle diameter is most common among the farm's currently-registered printers** (auto-detected, no organization setting or operator input for this), deriving nozzle diameter, generic material type, a parsed print-time/weight estimate, and a rendered thumbnail — the slicer's actual output is discarded, and the part's file remains exactly the STL/3MF as uploaded. A successful slice lands in a **"ready for review"** state rather than saving directly: the operator sees the derived data, may attach or change a narrower Filament Profile/color-variant override now that a real result exists to narrow, then either confirms (saving the part immediately from the already-derived data) or discards it (nothing saved). None of this reopens *what* gets derived or lets the operator hand-edit the derived numbers — it only gates *whether* a successful result is committed. If no printer with a known nozzle diameter is registered yet, slicing has nothing to target and the add/replace is rejected outright, with no review state ever reached. Manually uploading an already-sliced file (G-code, or a 3MF that already contains one) is the one path where the file itself is retained and parsed in place, with its thumbnail extracted from its own embedded preview image (a standard slicer/firmware convention) — as a part's only file when no original model is available at all (e.g. a downloaded G-code), or as a replacement. **This review/confirm step applies uniformly to every file type, not only a slicer-derived result** — an already-sliced file's add/replace goes through the identical submit → processing → ready-for-review → confirm/discard shape, it just typically resolves near-instantly since reading embedded metadata needs no external process the way slicing does; there is no longer a separate one-step synchronous path for it. **Attaching the file (plus a Filament pick, if needed) kicks off processing immediately, as one continuous add-part experience** — the operator keeps entering/editing the part's name and tags in the same dialog while processing runs in the background; only the confirm/save action itself is gated on processing having reached "ready for review" (and, for a new part, a name having been entered).
- **A part's filament requirement is a collection, not a single value**, so a real multi-color/AMS part is representable: an already-sliced file is scanned across every plate and every AMS slot within each for its detected material + color, and the results are **grouped by distinct (material type, color) pairs** — one filament requirement per distinct pair, not one per slot/plate, so e.g. two plates that are both black PETG collapse into a single requirement rather than duplicating. Each requirement is independently narrowable to an exact Filament Profile/color-variant, applying uniformly to every plate/slot sharing its pair. In-app slicing itself still only ever targets one operator-specified Filament (true multi-material *slicing* — assigning different materials to different regions of one model — is out of scope for this phase), so a part produced via automatic slicing always ends up with exactly one requirement even though the underlying data shape supports more.
- **There is no "job-ready" concept.** Every part that exists in the catalog is, by construction, fully valid and usable — nozzle diameter, material type, and an estimate are always present. The only status distinction a part carries is whether its file requires slicing (Phase 7 will need to slice it again for real toolpath data at job-dispatch time, since nothing sliceable was kept) or was already sliced on upload (its stored file can be sent to a printer directly).
- Uploading a new file for an existing part is always a **direct replacement** of its current one — never additive, and it either fully succeeds (file, thumbnail, and derived data all update together) or changes nothing at all if the new file fails to process. Running the same design against a different nozzle-diameter/material target means either replacing the file and re-slicing (discarding the previous variant) or creating a separate Part record for that variant; a Part never holds multiple simultaneous variants of itself.
- A part's compatibility match is **printer- and material-agnostic by default**: nozzle diameter and generic material type (e.g. "PETG") — parsed directly from an already-sliced upload, or derived from an in-app slice — are used as its baseline match, with any printer sharing the nozzle diameter, and any spool/filament of that generic material, qualifying, with **no concept of linking a part to a specific Machine Profile**. An operator can optionally narrow the material match to an exact Filament Profile/color-variant (Phase 2/3) — e.g. a Bambu multi-color 3MF's baked-in AMS slot-to-color mapping.
- A 3MF that happens to bundle multiple plates/objects is treated as one indivisible file belonging to one part, using only the first plate's image for the thumbnail, and is never split into multiple parts. **Filament-requirement detection does scan every plate**, however (grouped by distinct material+color pair, as above) — a genuine reopening of what was originally a blanket "no multi-plate handling at all" limitation, scoped specifically to filament requirements; a part's estimated print time/weight become the sum across all plates accordingly, since a multi-plate file prints them all as one continuous job.
- Tag parts (category, product line, etc.).
- A part's derived compatibility data needs no additional dependency on Phase 2's profile catalog beyond the required Filament itself; Phase 2 is only additionally needed for the optional narrower Filament Profile/color-variant override.
- **This phase is "whichever later phase actually consumes it"** referenced in Phase 1's own dependency note above: it finally wires the Printer ↔ Machine Profile link Phase 1/2 deliberately left deferred, reading a printer's nozzle diameter from its linked Machine Profile (which already models nozzle size) rather than a new duplicate field on `Printer`. The link is optional at printer-creation time (a matching Machine Profile might not be imported yet) — a printer can exist unlinked, it just doesn't count as a "real fleet member" for this phase's nozzle-diameter auto-detection (FR-005a/FR-005b in `specs/007-part-library/spec.md`).
- In-app slicing (engine + a file requiring slicing + a target material and auto-detected nozzle diameter → derived data + thumbnail, no persisted output) is built as a reusable capability, so Phase 7 (Jobs) can later invoke it on-demand at job-dispatch time to produce actual sliceable output for a job's exact target printer, without rebuilding the integration — this phase itself only wires up the automatic-at-upload, derive-only trigger; job-dispatch-time slicing is Phase 7's to build once a Job entity exists. Real job/printer matching at that later dispatch time depends on which printers and feed-slot materials are actually available then — a separate, dynamic concern from this phase's one-time, auto-detected slicing target.
- **No live printer connection required.** A part's file (whether requiring slicing, automatically sliced, or manually uploaded) and derived data are all catalog/slicing-pipeline data — no live printer *connection* needed, though nozzle-diameter auto-detection does read the printer catalog; only *using* an already-sliced file in a job (Phase 7) involves an actual printer.

## Phase 5 — Fleet hardening + real RBAC

- Printer tags (location, capability, farm zone, etc.) — the same tags double as scoping for Operator/Viewer roles and, later, circuit grouping (see Phase 11 — Intelligent queue matching).
- Feed configuration field: single spool, AMS, auto-reload/splicer, or a combination — this is what unblocks Phase 3's auto-reload branch, noted there as a deliberate forward dependency.
- `AwaitingClear` UX polish: a finished/failed/cancelled job leaves its printer in awaiting clear, not idle. A quick, explicit "Mark ready" action moves it to idle, which is the only state a printer can be assigned a new job from or picked up by a queue-matching suggestion. This is the state machine's `TransitionTo` path built in Phase 1.
- Swap Phase 0's placeholder permission shim for the real seeded permission catalog + four roles (Owner/Admin/Operator/Viewer); Operator and Viewer can optionally be scoped to specific printer tags rather than the whole farm.

## Phase 6 — Assembly (BOM)

- Build assemblies from multiple parts plus quantities.
- Support nested assemblies (an assembly can include other assemblies as sub-components).

## Phase 7 — Jobs / print queue (full)

- Builds the full job entity from scratch — `{printer_id, file, status}` plus BOM decomposition, formal sliced-file locking, and per-spool filament usage breakdown. There's no thin predecessor to extend now that the old ad-hoc print-flow phase was replaced by Profiles — this is the first job entity in the plan.
- Create jobs targeting a part or assembly, with quantity and priority. Quantity can also be **unlimited** — a standing job that keeps producing rather than completing once a target count is hit, e.g. an evergreen part a printer falls back to whenever otherwise idle. A job targeting an assembly decomposes into part-level sub-jobs following the BOM quantities, recursively through nested assemblies — each component part needs its own matching sliced file for whichever printer it ends up on.
- A job — unlimited or not — locks onto a copy/reference of the actual print-ready content it dispatches with, rather than dynamically following whatever the part's file currently is. If the part's file is later replaced while the job is sitting in the queue or still running, the job keeps printing the content it already locked onto (see Phase 4 — Part library, where a Part holds exactly one file and every upload after the first replaces it); it won't silently swap to a newer file mid-batch. A per-job option to instead always track the part's latest file — most useful for unlimited jobs meant to run indefinitely — is a reasonable future addition, but isn't everyone's default, so it's deferred rather than built now (see Open decisions).
- Creating a job requires nothing beyond the part existing — every part in the catalog already has real nozzle diameter and generic material type by construction (Phase 4 never saves an incomplete part), so there's no separate "is this part ready" gate to check. What matters is whether those values match the target printer and the assigned spool's material, same as any other job. Sending the job to a printer is where the file-origin distinction bites: if the part's file was uploaded already-sliced, its stored file is sent directly; if the part's file still requires slicing (Phase 4 only ever derived an estimate and thumbnail from it, never a kept sliced output), Phase 4's reusable in-app-slicing capability is invoked on-demand at dispatch time to produce real toolpath data for that job's exact target printer — understanding that this doesn't persist onto the part itself, since a Part holds only one file at a time (see Phase 4 — Part library) and this on-demand slice is scoped to the job, not stored as the part's new file. A queued job whose part's nozzle diameter or material doesn't match the target printer/spool is flagged clearly rather than blocking job creation itself.
- Manual status workflow: queued → printing → paused → failed → completed → cancelled. Reaching completed, failed, or cancelled moves the printer to **awaiting clear** rather than idle (see Phase 1 — Fleet management + live Bambu integration) — the queue only offers a printer its next job once it's back to idle.
- Unlimited queue depth that stays fast to browse as it grows.
- Each job has a required material/color, defaulted from the part but overridable per job.
- For connected Bambu printers, live integration can auto-detect job start/finish using the same status-observation machinery Phase 1 built, and sending a job pushes the matched sliced file to the printer directly; other brands require the operator to retrieve the sliced file (e.g. via USB/SD) and start the print manually, then mark it in progress — Phase 14 — Expanded live printer integration extends automatic push and detection to those brands too.
- Filament consumed by a job is recorded as a breakdown per spool/profile rather than a single total — a multi-color AMS job might draw from three different spools, and usage analytics later need to attribute grams to the right material and color, not just to the job as a whole.

## Phase 8 — Maintenance tracking

- Maintenance task templates triggered by calendar interval, print hours, or job count.
- Manual maintenance logs: performed by, date, notes, cost, parts replaced.
- Alerts when a printer crosses a maintenance threshold.
- Depends on Phase 1's printer entities existing (thresholds and logs attach to a specific printer).

## Phase 9 — Dashboard

- At-a-glance view: printer statuses (including live Bambu status and printers awaiting clear), active jobs, low-filament alerts, maintenance due.

## Phase 10 — Backup & restore

- Full export of a single organization's farm data (printers, filament, profiles, parts, assemblies, jobs, maintenance history) as one downloadable file, triggerable from Settings by Owner or Admin.
- Restore from a previous export into a fresh install — covers both disaster recovery and moving to new hardware.
- Connection secrets are excluded from the default export and re-entered after a restore; an optional "include credentials" export mode encrypts them with a passphrase set at export time instead of shipping them in the clear.
- Exporting cleanly by organization, even before multi-tenancy is user-facing, is a useful forcing function for confirming the data model actually keeps tenants separable.

## Phase 11 — Intelligent queue matching

- Suggests which printer to assign a queued job to based on currently loaded filament (material + color match), to minimize spool swaps.
- Operator confirms suggestions rather than the system auto-claiming a machine.
- Only printers in the idle state (not awaiting clear) are considered candidates.
- When several suggested assignments dispatch at once, printer starts are staggered by a configurable delay rather than firing simultaneously, to avoid tripping a shared circuit. Printers can optionally be tagged into circuit groups (reusing the existing tag system) so staggering only applies within a shared circuit, not farm-wide.
- Later: factor in printer capability (from machine profiles, Phase 2 — Profiles — nozzle size, bed volume, enclosure) so impossible assignments are never suggested.
- Later: optimize across the whole queue at once, not just job by job.

## Phase 12 — Optional spool identification

- Barcode/RFID scanning for anyone who wants faster, scan-based spool identification, layered on top of the same spool records — never required, since the default lifecycle already works without one.

## Phase 13 — Spoolman-compatible filament API

- OpenFarm keeps owning its own filament data (needed for multi-tenancy and for linking spools to jobs, slots, and auto-reload logic), but exposes spool/filament/vendor endpoints matching Spoolman's REST API shape.
- This means Moonraker's built-in Spoolman integration (for Klipper-based printers), Home Assistant's Spoolman integration, and existing NFC hardware built against Spoolman (scanning scales, tag readers) work against OpenFarm without modification.
- A one-time import tool pulls vendors, filaments, and spools from an existing Spoolman instance, for farms migrating over rather than starting cold.

## Phase 14 — Expanded live printer integration

- Extend live integration beyond Bambu Lab to other brands/protocols (OctoPrint, Klipper/Moonraker, Prusa Connect, etc.), bringing them up to the same auto-status-detection level Bambu Lab got in Phase 1 — Fleet management + live Bambu integration.

## Phase 15 — Order integration

- Auto-create jobs from marketplace orders (Shopee, TikTok Shop, Lazada).
- Link jobs back to the originating order for fulfillment tracking.

## Phase 16 — Notifications

- Telegram, Discord, or email alerts for maintenance due, job completed, or low filament.

## Phase 17 — Analytics dashboard

- Configurable, card-based layout — users choose which metrics to show rather than a fixed set.
- Metrics available as cards: filament usage (by material, color, printer, or time period), printer utilization rate, total print time, idle time, cost per part/assembly/job.
- Optional selling price per part/assembly, with margin (price minus estimated cost) surfaced next to the existing cost metrics. Estimated cost draws from filament cost-per-gram (from spool purchase price) and a configurable per-printer hourly machine rate, giving a quick quote for a given part and quantity without needing full order integration.
- Because the underlying data (printer status history since Phase 1, per-job filament breakdown since Phase 7) has already been captured, this is mostly aggregation and UI work rather than a data model change.

## Phase 18 — Multi-tenant SaaS infrastructure

- Organization switching, team invites, billing.

## Phase 19 — Public API / webhooks

- Public API / webhooks for community integrations.

## Phase 20 — Predictive maintenance

- Predictive maintenance based on usage patterns.

## Phase 21 — Plugin / extension ecosystem

- Plugin/extension ecosystem for the open-source community.

## Phase 22 — Local connector agent

- Required before hosted SaaS can offer live printer integration at all — self-hosted installs never need this, since OpenFarm already runs on the farm's own network there.
- A lightweight agent installed on the customer's local network (one per farm/site) connects outward to the OpenFarm cloud backend, avoiding any inbound firewall/port-forwarding setup on the customer's side, and relays printer connectivity (Bambu LAN today, other brands as Phase 14 — Expanded live printer integration expands) the same way the self-hosted deployment does directly.
- Authenticates as a machine identity scoped to the organization, distinct from a user login, with a narrow permission surface (report telemetry, receive commands) rather than the full user role/permission set.
- Reuses the same printer-connector boundary built in Phase 1 — Fleet management + live Bambu integration — only the transport (direct LAN vs. relayed through the agent) differs, not the per-brand connection logic itself, so this doesn't require rebuilding earlier printer integration work.

## Phase 23 — Camera recording & timelapse

- A minimal *live* camera view already ships in Phase 1 (M0.7) — this phase covers what that doesn't: **optionally recording** and linking timelapses/snapshots back to the job that produced them.
- Connection details reuse the same flexible per-printer structure set up in Phase 1 — Fleet management + live Bambu integration.

## Out of scope for now

- Native mobile app (the webapp will be responsive-first).

## Open decisions

- License model for eventual public release (permissive vs. source-available/open-core) — worth deciding before the first public commit.
- Whether self-hosting or hosted SaaS is the priority distribution model post-MVP.
- Bambu Lab's LAN-only + Developer Mode requirement disables that printer's connection to Bambu Cloud/Handy while active — decide how clearly to surface this trade-off to users before they connect a printer. This is a real, immediate trade-off for Phase 1's own development, not just a future user-facing concern.
- Sourcing default printer/filament profile data is resolved by design: Phase 2 is import-your-own-bundle only, so OpenFarm never bundles or needs to license third-party slicer profile data as defaults.
- Retention/aggregation strategy for the printer status-history log (Phase 1) once it's been running for a while — raw timestamped events are fine to start, but worth revisiting before the table grows large, and before Phase 17 — Analytics dashboard leans on it heavily.
- HMS hardware-fault/alert surfacing (Phase 1) — the printer's live connection already reports an active fault list (severity + subsystem) and a fatal print-error code, but Phase 1 deliberately doesn't model or display either; decide which later phase owns surfacing it (Phase 8 — Maintenance tracking and Phase 16 — Notifications are the two natural homes) rather than letting it stay silently unaddressed.
- Key management for connection-secret encryption: an install-generated key is enough for self-hosted, but the future hosted multi-tenant version (Phase 18) should move to a proper secrets-manager/KMS-backed key, likely per-tenant — worth deciding before the first hosted deployment, not urgent for MVP.
- Whether jobs (particularly unlimited-quantity ones) should ever get a per-job toggle to track a part's latest sliced file live instead of the one they locked onto at queue time — default stays snapshot either way; this is only about whether an opt-in live mode gets built at all, and isn't needed for MVP.
- Local connector agent's transport, machine-credential format, and pairing/rotation flow (how a customer installs and authorizes an agent for their org) aren't decided — not needed for MVP, but the printer connector should still be built as a swappable boundary in Phase 1 — Fleet management + live Bambu integration so this doesn't force a rewrite later.
- Bambu's local MQTT/Developer Mode access is unofficial-but-permitted, not a versioned/guaranteed API — firmware updates have previously narrowed third-party access. Worth an explicit maintenance-risk note for `BambuLanConnector`, not a one-time build-it-and-forget item.
