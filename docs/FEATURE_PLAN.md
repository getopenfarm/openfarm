# OpenFarm — Feature Plan

## Overview

OpenFarm is a print farm management webapp covering printers, filament, maintenance, parts, assemblies (BOMs), and print jobs. It's being built open-source-first, kept private for now, and architected to support multi-tenancy from day one in case of future monetization.

## Guiding principles

- **MVP-first, not data-model-first.** Ship a real, demoable capability as early as possible and let later phases extend it, rather than building the full schema before anything is usable. The previous version of this plan required filament tracking, printer profiles, and sliced files to all exist before a printer could even be added to the fleet — this version inverts that: fleet management and live Bambu Lab connectivity are the *first* thing built, because they're the most visible, most demoable capability and the Bambu local-MQTT integration path is well-understood, not R&D-risk territory (see Phase 1).
- **Ordered by day-to-day print-farm operational value, not just build convenience.** After Phase 1 (which is both a structural prerequisite for almost everything else *and* the single most-checked piece of information in a real farm), phases are sequenced by what an actual farm operator needs next in practice — being able to run something, then not running out of filament mid-job — ahead of internal org/permission hygiene that doesn't affect daily operation. See Phases 2–4 below and the Dependency notes section for the reasoning.
- Usable by a solo operator or small farm without multi-tenant/SaaS complexity getting in the way.
- Automation is *suggested*, not forced — a human still physically loads spools and parts, so the system should assist decisions rather than take them over.
- Every feature should work for a self-hosted single-farm install and a future hosted multi-tenant version without redesign.
- TDD is applied selectively, not uniformly: deterministic domain logic (state machines, BOM decomposition, filament weight math) gets red-green-refactor; orchestration-only application code, EF Core plumbing, the real Bambu MQTT client, and React components are test-after or fake/fixture-based instead. See Phase 1's sub-milestones for a concrete example of where this split falls.

## Phase groupings (for orientation only)

The structure below is intentionally flat — every phase is its own top-level section so any one of them can get a full detail/dev plan without dragging a dozen unrelated features along with it. These groupings are just informal labels for orientation, not a re-introduction of nested phases:

- **Foundation:** Phase 0
- **Core MVP:** Phases 1–12
- **Smart features & integrations:** Phases 13–19
- **Scale & monetization:** Phases 20–26

## Dependency notes

**Core MVP (1–12) is ordered by a combination of demo-ability and real print-farm operational importance, not data-model completeness or internal build convenience:**
- **Phase 1** (Fleet management + live Bambu integration, including a live camera feed) is built first and deliberately ships *without* a separate printer-profiles table — a printer's spec fields are inlined directly on the printer entity. The old plan required a printer-profiles template to exist before a printer instance could be tracked at all; that rule protected against duplication that hasn't happened yet (one operator, one or two printers) and is deferred until Phase 4, once a second printer/model actually creates evidence of it being worth extracting. Phase 1 is also the single structural prerequisite almost everything else in this plan depends on — every other phase either attaches to a printer row or reads its status history.
- **Phase 2** (minimal print-flow) reaches a *second* demoable capability — actually pushing a file to a printer and starting a print — as fast as possible, by bypassing parts/assemblies/BOM/filament-profiles entirely. This is ranked ahead of fleet hardening/RBAC deliberately: turning "watch the fleet" into "operate the fleet" is a bigger jump in real farm value than tags or permission scoping, and it has no dependency on either. It's a deliberately thin, throwaway-shaped job entity that Phase 9 later extends into the real thing, rather than gating this capability behind the full data model.
- **Phase 3** (Filament tracking) is ranked ahead of fleet hardening/RBAC for the same operational-value reason: a spool running out mid-print is the most common real-world failure a farm operator deals with day to day, and basic spool tracking (add/track/mark-finished) needs zero printer knowledge at all. The one exception is called out explicitly in Phase 3 itself: its *auto-reload* branch needs to know which feed slots are configured as auto-reload, and that field doesn't exist until Phase 4 — core filament tracking isn't blocked on Phase 4, only that one behavior is.
- **Phase 4** (Fleet hardening + real RBAC) — tags, feed-config, printer-profiles extraction, and the real permission catalog — moves here specifically *because* it's org/multi-user hygiene rather than something a solo/small-farm operator needs to see working day to day. It's real, necessary work, just not more urgent than Phases 2–3.
- **Phases 5–8** (Filament profiles, Part library, Assembly/BOM, Sliced files) need zero printer knowledge — no API, no file format, no live connection — beyond what Phase 4 already provides for Phase 8's printer-profile linkage, and can be built in parallel with or after Phases 1–4 without further blocking.
- **Phase 8** (Sliced files, formalized) depends on Phase 4's printer profiles and Phase 5's filament profiles now existing, and turns Phase 2's ad-hoc file upload into a proper sliced-file record.
- **Phases 9–12** (Jobs, Maintenance, Dashboard, Backup & restore) tie everything above together and stay last, same as the original plan.

**Filament spools still do not depend on a filament profile** (Phase 3 vs. Phase 5) — a spool can be added and tracked through in storage / using / finished with no profile attached; a profile is only required once the spool is actually used in a job.

**Smart features and scale phases carry the same dependencies they always did:**
- Phases 13–19 mostly depend only on their Phase 1–12 prerequisites (e.g. Phase 13 needs Phase 1's idle state and Phase 9's queue; Phase 19 needs the data capture that starts in Phase 1 and Phase 9).
- Phases 20–26 build on the swappable printer-connector boundary from Phase 1 and the multi-org groundwork from Phase 0.

## Phase 0 — Foundation (prerequisite, not user-facing)

**Status: Delivered.** Implemented via `specs/001-foundation/` (spec, plan, tasks) — real JWT
login for the seeded operator, auto-created single organization, encrypted/masked/immutable
connection secrets, the structural activity log, and the base shadcn UI kit are all live.
Phase 1 can now build on top of it.

Scoped down deliberately for the MVP-first pivot — each piece below is cut independently based on whether stubbing it is defensible or a corner that bites us later, not lumped into one all-or-nothing "foundation" block:

- **Auth: real, but coarse.** One seeded user, real JWT-based login, no self-registration, no role-management UI. A no-auth throwaway slice was considered and rejected — this system stores real Bambu printer access codes and the activity log needs a real actor, so retrofitting auth later would mean unwinding both.
- **Multi-org: schema now, resolution machinery later.** `organization_id` is present on every tenant-scoped table from day one — adding it retroactively would mean a migration touching every existing row plus a real risk window of a forgotten tenant filter. What's deferred is the actual dynamic claim-based resolution (Finbuckle.MultiTenant): for a single auto-created organization, a static single-tenant resolver is enough until Phase 20 needs real org-switching.
- **RBAC: placeholder policy shim now, real catalog in Phase 4.** A thin permission-shaped policy attribute is added to endpoints from the start (so it doesn't need retrofitting later), but its handler just checks "authenticated" until Phase 4 swaps in the real seeded permission catalog and four roles (Owner/Admin/Operator/Viewer, Operator/Viewer optionally scoped to printer tags — see Phase 4).
- **Connection secrets: real encryption from day one, not stubbed.** These rows hold a real physical printer's LAN access code — a working credential, not a demo fixture. Encrypted at rest using ASP.NET Core's `IDataProtector` (no external KMS needed for self-host) from the first commit; moving to a proper secrets-manager/KMS-backed per-tenant key is deferred to Phase 20 (hosted SaaS), not before.
- **Activity log: built for real.** A generic, polymorphic activity log (entity, event type, actor, timestamp) underlies every status change from the start — this is what Phase 1's printer status-change logging and Phase 19's analytics both read from, and it needs to start accumulating from day one rather than being bolted on retroactively.

## Phase 1 — Fleet management + live Bambu integration

**The first feature built after the scaffold.** Combines what the previous plan split across "Fleet management," "Live printer integration," and "Camera / live monitoring" into one phase, because the whole point of building fleet management first is to make it live and visible from the start rather than a static CRUD screen that gets live status (and, later, video) bolted on much later.

- Printer entity with spec fields **inlined directly** (name, model, IP address, serial number, connection-secret reference) — no separate printer-profiles table yet (see Dependency notes above; extracted in Phase 4).
- Printer status modeled as an explicit domain state machine: `Idle` / `Printing` / `AwaitingClear`, split into two operations — a strict `TransitionTo` for human-initiated commands (e.g. "Mark ready") that throws on an illegal manual jump, and a lenient `ObserveStatus` for connector-reported physical reality, which reconciles and logs rather than rejecting a fact the printer is already doing (e.g. someone started a print from the Bambu app directly). `Offline`/connectivity is tracked separately as connector connection state, not as a `PrinterStatus` member. `Maintenance` is deferred until Phase 10 exists to enter it.
- `IPrinterConnector` interface, with a `FakePrinterConnector` so application code and UI can be built and tested well before the real connector is finished.
- A real `BambuLanConnector` over MQTTnet, connecting to Bambu X1/P1/A1-series printers in LAN-only + Developer Mode: MQTT over TLS on port 8883 (topic `device/<serial>/report` for status/telemetry/AMS-slot pushes), validated against a physical printer.
- SignalR push (`/hubs/farm`) into a minimal fleet-list + printer-detail UI — status/telemetry updates live without a page refresh.
- A minimal live camera view on the printer-detail page (RTSP-ish stream on port 322 for X1/H2/P2, port 6000 for P1/A1) — folded in here rather than left until the old Phase 26, since it reuses the exact same LAN connection/connector boundary and adds a much more visceral "it's really working" demo moment for close to no extra cost. Recording/timelapse linkage stays a later phase (see Phase 26).
- **Scope for this phase's demo bar: live monitoring only** (status, telemetry, AMS slots, camera feed). No print-trigger or file push yet — that's Phase 2. The goal here is "watch the fleet update live off a real printer," not "make it do something."
- Every status change and connector reconciliation is logged via the Phase 0 activity log.

### Sub-milestones for this phase

Sequencing logic: a small throwaway hardware spike first (de-risks the one genuinely uncertain element — the real Bambu MQTT protocol — before committing to interface shape), then a thin vertical slice (visible CRUD before the real connector, real connector before real-time push) rather than a full backend-then-UI build-out.

- **M0.0 — Add reverse-engineered Bambu API reference as a git submodule.** The user has already gathered multiple sources of reverse-engineered Bambu Lab API documentation/code (protocol details, message shapes, MQTT/FTP/camera behavior across models) from prior investigation. This is added as a git submodule and used as the primary protocol reference — supersedes/supplements the public community projects noted below.
  **TODO — placeholder, not yet filled in:** exact submodule path (proposed: `docs/reference/bambu-api/`) and source repo URL(s) are to be specified by the user before implementation begins. Do not run `git submodule add` until the URL(s) are provided.
  For context, public prior art confirming this integration path is well-trodden (not R&D-risk territory): [Bambu.NET](https://github.com/ColdThunder11/Bambu.NET) (MQTTnet-based `BambuMQTTClient`, closest match to this project's C#/.NET stack), [BambuSharp](https://libraries.io/nuget/BambuSharp) (NuGet, wraps temps/progress/AMS state), and [bambulabs_api](https://pypi.org/project/bambulabs-api/) (Python, documents MQTT/FTP/camera behavior in detail even though it's a different language).
- **M0.1 — Bambu MQTT spike (throwaway, no product code).** Raw MQTTnet client against the physical printer in LAN-only + Developer Mode: TLS connect on 8883, subscribe `device/<serial>/report`, dump raw JSON. Cross-reference against the M0.0 submodule's documented message shapes. Capture real fixture payloads for M0.5's tests.
- **M0.2 — Minimal-but-real plumbing.** Seeded user + JWT login, `Organization` singleton + static single-tenant resolver, `connection_secrets` table with real `IDataProtector` encryption, `activity_log` table + FastEndpoints post-processor proven against one dummy mutation.
- **M0.3 — Printer domain + CRUD (no live connector yet).** `Printer` entity, `PrinterStatus` state machine — **TDD**: `TransitionTo` + `ObserveStatus`, all transition pairs, invalid-transition exceptions, reconciliation/logging behavior on a physical-reality mismatch. FluentValidation, FastEndpoints CRUD endpoints, EF Core migration.
- **M0.4 — Minimal UI.** Fleet list + add-printer form (react-hook-form + zod) + detail/status view, wired to the real Orval-generated client. Plain TanStack Query polling — no SignalR yet.
- **M0.5 — Real `BambuLanConnector`.** Built from M0.1's fixtures behind `IPrinterConnector`, developed against `FakePrinterConnector` first. **Test-after / fake-based**: fixture-replay unit tests (real captured JSON → parsed telemetry/AMS-slot model), in-process MQTT broker tests for subscribe/publish wiring. **Manual verification only, not in CI:** final validation against the physical printer — TLS handshake, reconnect-after-drop, AMS accuracy.
- **M0.6 — SignalR live wiring.** `/hubs/farm` pushes status/telemetry, frontend subscribes into the TanStack Query cache — the actual demo moment: the fleet page updates live off a real printer.
- **M0.7 — Minimal live camera view.** Render the printer's local video stream (port 322 or 6000, per model) on the printer-detail page — no recording/timelapse yet (see Phase 26).

### Risks/notes

- Enabling LAN-only + Developer Mode disconnects the printer from Bambu Cloud/the official app while active — a real workflow disruption during development, not just a footnote, since it's the user's own physical printer.
- Local MQTT access is Bambu-permitted but unofficial/unversioned — firmware updates have previously altered third-party access in the past. This is an ongoing maintenance risk for `BambuLanConnector`, not a one-time cost.
- Camera/live-video port differs by printer generation (322 for X1/H2/P2, 6000 for P1/A1); FTPS file transfer runs on port 990 and becomes relevant in Phase 2, not this phase.

## Phase 2 — Minimal "send an existing sliced file to a printer" job flow

- Deliberately bypasses parts, assemblies, BOM, filament profiles, and formal sliced-file records entirely — this is the fastest path to a *second* demoable capability, proven as a real workflow before the full data model is built under it. Ranked ahead of fleet hardening/RBAC (Phase 4) because turning "watch the fleet" into "operate the fleet" is core print-farm value, and this has no dependency on tags, permissions, or a printer-profiles table.
- Upload a pre-sliced 3MF/gcode file, pick a fleet printer, push it to the printer over FTPS (port 990) via `IPrinterConnector`, and issue a print-start command.
- Minimal job shape: `{printer_id, file, status}` with status `queued` / `printing` / `completed` / `failed` — no BOM decomposition, no per-spool filament usage breakdown, no sliced-file locking. Phase 9 later extends this same entity rather than replacing it.
- Printer status transitions to `Printing` (via `ObserveStatus` once the connector detects the print start) and to `AwaitingClear` on completion/failure, same as Phase 1's state machine.

## Phase 3 — Filament tracking

- Ranked ahead of fleet hardening/RBAC (Phase 4) for the same operational-value reason as Phase 2: a spool running out mid-print is the single most common daily operational failure on a real print farm, and the core lifecycle below needs zero printer knowledge. **One explicit exception:** the auto-reload branch needs to know which feed slot is configured as auto-reload, and that configuration field doesn't exist until Phase 4 — everything else in this phase works today, only that one behavior activates once Phase 4 lands.
- Spool tracking never requires a physical barcode/unique ID — an optional identifier can be added later for anyone who wants to scan spools, but it's never required to use the feature (see Phase 14 — Optional spool identification).
- Each spool has three states: **in storage** (not loaded), **using** (loaded onto a feed slot, actively tracked), and **finished** (depleted). Remaining weight is visible at the moment a spool is removed, so the operator can decide immediately whether it goes back to storage (still has usable filament) or gets marked finished — no in-between holding state needed.
- Remaining weight is estimated automatically from job usage while a spool is in use.
- For single-spool and AMS slots: marking a spool finished stays a manual action. A slot running out doesn't auto-continue, so the operator is already there physically replacing it — and pulling a spool doesn't always mean it's empty (could be a deliberate color change).
- For auto-reload slots (see the exception noted above, once Phase 4's feed-config field exists): running out is exactly the moment the device is built to react to, so the system can detect it directly from the numbers — once cumulative job usage catches up to a spool's starting weight, mark it finished and flip the active slot to the standby spool automatically, without waiting on a manual mark. This is still an estimate rather than a precise measurement, so a manual override should stay available in case actual usage drifts from it over time.
- Before flipping to the standby spool, the system checks that its assigned filament profile matches the job's required material and color. A mismatch doesn't pause the swap — the entire point of auto-reload is continuing unattended — but it raises a dashboard warning and, once notifications exist (see Phase 18 — Notifications), an alert, and the mismatch is logged against the job so it's traceable afterward rather than silent.
- Internally modeled with a schema deliberately close to Spoolman's (spool / filament / vendor), even though this data lives natively in OpenFarm rather than depending on a separate Spoolman instance — see Phase 15 — Spoolman-compatible filament API for why.
- **No printer knowledge required** for the core lifecycle. Everything above (except the noted auto-reload exception) works identically regardless of which printer brand or model eventually loads the spool.

## Phase 4 — Fleet hardening + real RBAC

- Printer tags (location, capability, farm zone, etc.) — the same tags double as scoping for Operator/Viewer roles and, later, circuit grouping (see Phase 13 — Intelligent queue matching).
- Feed configuration field: single spool, AMS, auto-reload/splicer, or a combination — this is what unblocks Phase 3's auto-reload branch, noted there as a deliberate forward dependency.
- `AwaitingClear` UX polish: a finished/failed/cancelled job leaves its printer in awaiting clear, not idle. A quick, explicit "Mark ready" action moves it to idle, which is the only state a printer can be assigned a new job from or picked up by a queue-matching suggestion. This is the state machine's `TransitionTo` path built in Phase 1.
- Extract `printer_profiles` into its own reusable table (nozzle size, bed volume, enclosure, supported materials, whether the model supports live integration) — justified now that a second printer/model creates real duplication, unlike at Phase 1's start.
- Swap Phase 0's placeholder permission shim for the real seeded permission catalog + four roles (Owner/Admin/Operator/Viewer); Operator and Viewer can optionally be scoped to specific printer tags rather than the whole farm.

## Phase 5 — Filament profiles

- Reusable spec template per material, brand, and — where it actually matters — color (e.g. "Bambu PLA Basic – Black" can have its own tuned settings rather than sharing one generic profile across every color of that line). A color-specific profile can inherit from a shared base profile and only override what's different.
- A spool must have a profile assigned before it can be used in a print job — but, per the dependency note above, not before the spool itself can be added and tracked (Phase 3).
- Ship a small set of generic starter profiles; also let users import their own profile data from their existing slicer setup rather than depending on us to source and maintain brand-specific values ourselves.
- For connected Bambu printers, live AMS slot contents can be cross-checked against the filament profile catalog once that exists (Phase 1 — Fleet management + live Bambu integration).

## Phase 6 — Part library

- Add parts with file, thumbnail, material profile, estimated print time, and estimated filament weight.
- The uploaded file is treated as the reusable source model (STL/3MF) rather than a pre-sliced, printer-specific output — worth being explicit about now so in-app slicing can be added later (see Phase 25 — In-app slicing) without reinterpreting existing parts.
- Simple revision tracking. Revising a part's source model flags its existing sliced files (see Phase 8 — Sliced files) as potentially stale rather than invalidating them outright — a re-slice or re-upload is prompted, not forced immediately.
- Tag parts (category, product line, etc.).
- **No printer knowledge required.** A part's source model and estimates are printer-agnostic; only its sliced files (Phase 8) are printer-specific.

## Phase 7 — Assembly (BOM)

- Build assemblies from multiple parts plus quantities.
- Support nested assemblies (an assembly can include other assemblies as sub-components).

## Phase 8 — Sliced files (formalized)

- A part needs at least one **sliced file** (G-code, 3MF, or brand-specific bundle) before a job targeting it can be sent to a printer — distinct from the reusable source model in the Part library (Phase 6), and from printer/filament profiles (Phase 4, Phase 5).
- Each sliced file is tied to a specific printer profile (slicer settings depend on nozzle, bed, and material) and, where relevant, a filament profile (a Bambu multi-color 3MF bakes in AMS slot-to-color mapping). Any printer sharing that profile can use the same sliced file.
- A part can have multiple sliced files, one per printer profile it needs to run on — mirrors how a production run splits across different printer models in practice.
- Estimated time and filament weight are parsed from the file where possible, otherwise fall back to the manual estimates already on the Part.
- Turns Phase 2's ad-hoc file upload into a proper sliced-file record — this is also where the target printer's real file-format quirks (e.g. Bambu's 3MF AMS mapping) get formalized, building on what Phase 2 already had to work out informally.
- Phase 25 — In-app slicing generates these same records automatically from the source model instead of requiring manual upload — no restructuring needed when that lands.

## Phase 9 — Jobs / print queue (full)

- Extends Phase 2's minimal job entity rather than replacing it: adds BOM decomposition, formal sliced-file locking, and per-spool filament usage breakdown on top of the same `{printer_id, file, status}` shape.
- Create jobs targeting a part or assembly, with quantity and priority. Quantity can also be **unlimited** — a standing job that keeps producing rather than completing once a target count is hit, e.g. an evergreen part a printer falls back to whenever otherwise idle. A job targeting an assembly decomposes into part-level sub-jobs following the BOM quantities, recursively through nested assemblies — each component part needs its own matching sliced file for whichever printer it ends up on.
- A job — unlimited or not — locks onto a specific sliced file when it's queued, rather than dynamically following whatever the part's current sliced file is. If the part gets revised while the job is sitting in the queue or still running, the job keeps using the sliced file it already has (consistent with sliced files being flagged stale, not replaced, on revision — see Phase 6 — Part library); it won't silently swap revisions mid-batch. A per-job option to instead always track the part's latest sliced file — most useful for unlimited jobs meant to run indefinitely — is a reasonable future addition, but isn't everyone's default, so it's deferred rather than built now (see Open decisions).
- Sending a job to a printer requires the printer's profile, the assigned spool's profile, and a sliced file matching that printer's profile (see Phase 8 — Sliced files) to all be in place; the job can still be queued beforehand, and a queued job missing a sliced file is flagged clearly ("missing sliced file for [profile]") rather than blocking job creation itself.
- Manual status workflow: queued → printing → paused → failed → completed → cancelled. Reaching completed, failed, or cancelled moves the printer to **awaiting clear** rather than idle (see Phase 1 — Fleet management + live Bambu integration) — the queue only offers a printer its next job once it's back to idle.
- Unlimited queue depth that stays fast to browse as it grows.
- Each job has a required material/color, defaulted from the part but overridable per job.
- For connected Bambu printers, live integration can auto-detect job start/finish (as already proven by Phase 2), and sending a job pushes the matched sliced file to the printer directly; other brands require the operator to retrieve the sliced file (e.g. via USB/SD) and start the print manually, then mark it in progress — Phase 16 — Expanded live printer integration extends automatic push and detection to those brands too.
- Filament consumed by a job is recorded as a breakdown per spool/profile rather than a single total — a multi-color AMS job might draw from three different spools, and usage analytics later need to attribute grams to the right material and color, not just to the job as a whole.

## Phase 10 — Maintenance tracking

- Maintenance task templates triggered by calendar interval, print hours, or job count.
- Manual maintenance logs: performed by, date, notes, cost, parts replaced.
- Alerts when a printer crosses a maintenance threshold.
- Depends on Phase 1's printer entities existing (thresholds and logs attach to a specific printer).

## Phase 11 — Dashboard

- At-a-glance view: printer statuses (including live Bambu status and printers awaiting clear), active jobs, low-filament alerts, maintenance due.

## Phase 12 — Backup & restore

- Full export of a single organization's farm data (printers, filament, profiles, parts, assemblies, jobs, maintenance history) as one downloadable file, triggerable from Settings by Owner or Admin.
- Restore from a previous export into a fresh install — covers both disaster recovery and moving to new hardware.
- Connection secrets are excluded from the default export and re-entered after a restore; an optional "include credentials" export mode encrypts them with a passphrase set at export time instead of shipping them in the clear.
- Exporting cleanly by organization, even before multi-tenancy is user-facing, is a useful forcing function for confirming the data model actually keeps tenants separable.

## Phase 13 — Intelligent queue matching

- Suggests which printer to assign a queued job to based on currently loaded filament (material + color match), to minimize spool swaps.
- Operator confirms suggestions rather than the system auto-claiming a machine.
- Only printers in the idle state (not awaiting clear) are considered candidates.
- When several suggested assignments dispatch at once, printer starts are staggered by a configurable delay rather than firing simultaneously, to avoid tripping a shared circuit. Printers can optionally be tagged into circuit groups (reusing the existing tag system) so staggering only applies within a shared circuit, not farm-wide.
- Later: factor in printer capability (from printer profiles, Phase 4 — Fleet hardening — nozzle size, bed volume, enclosure) so impossible assignments are never suggested.
- Later: optimize across the whole queue at once, not just job by job.

## Phase 14 — Optional spool identification

- Barcode/RFID scanning for anyone who wants faster, scan-based spool identification, layered on top of the same spool records — never required, since the default lifecycle already works without one.

## Phase 15 — Spoolman-compatible filament API

- OpenFarm keeps owning its own filament data (needed for multi-tenancy and for linking spools to jobs, slots, and auto-reload logic), but exposes spool/filament/vendor endpoints matching Spoolman's REST API shape.
- This means Moonraker's built-in Spoolman integration (for Klipper-based printers), Home Assistant's Spoolman integration, and existing NFC hardware built against Spoolman (scanning scales, tag readers) work against OpenFarm without modification.
- A one-time import tool pulls vendors, filaments, and spools from an existing Spoolman instance, for farms migrating over rather than starting cold.

## Phase 16 — Expanded live printer integration

- Extend live integration beyond Bambu Lab to other brands/protocols (OctoPrint, Klipper/Moonraker, Prusa Connect, etc.), bringing them up to the same auto-status-detection level Bambu Lab got in Phase 1 — Fleet management + live Bambu integration.

## Phase 17 — Order integration

- Auto-create jobs from marketplace orders (Shopee, TikTok Shop, Lazada).
- Link jobs back to the originating order for fulfillment tracking.

## Phase 18 — Notifications

- Telegram, Discord, or email alerts for maintenance due, job completed, or low filament.

## Phase 19 — Analytics dashboard

- Configurable, card-based layout — users choose which metrics to show rather than a fixed set.
- Metrics available as cards: filament usage (by material, color, printer, or time period), printer utilization rate, total print time, idle time, cost per part/assembly/job.
- Optional selling price per part/assembly, with margin (price minus estimated cost) surfaced next to the existing cost metrics. Estimated cost draws from filament cost-per-gram (from spool purchase price) and a configurable per-printer hourly machine rate, giving a quick quote for a given part and quantity without needing full order integration.
- Because the underlying data (printer status history since Phase 1, per-job filament breakdown since Phase 9) has already been captured, this is mostly aggregation and UI work rather than a data model change.

## Phase 20 — Multi-tenant SaaS infrastructure

- Organization switching, team invites, billing.

## Phase 21 — Public API / webhooks

- Public API / webhooks for community integrations.

## Phase 22 — Predictive maintenance

- Predictive maintenance based on usage patterns.

## Phase 23 — Plugin / extension ecosystem

- Plugin/extension ecosystem for the open-source community.

## Phase 24 — Local connector agent

- Required before hosted SaaS can offer live printer integration at all — self-hosted installs never need this, since OpenFarm already runs on the farm's own network there.
- A lightweight agent installed on the customer's local network (one per farm/site) connects outward to the OpenFarm cloud backend, avoiding any inbound firewall/port-forwarding setup on the customer's side, and relays printer connectivity (Bambu LAN today, other brands as Phase 16 — Expanded live printer integration expands) the same way the self-hosted deployment does directly.
- Authenticates as a machine identity scoped to the organization, distinct from a user login, with a narrow permission surface (report telemetry, receive commands) rather than the full user role/permission set.
- Reuses the same printer-connector boundary built in Phase 1 — Fleet management + live Bambu integration — only the transport (direct LAN vs. relayed through the agent) differs, not the per-brand connection logic itself, so this doesn't require rebuilding earlier printer integration work.

## Phase 25 — In-app slicing

- Slice a part's source model directly in OpenFarm using its assigned printer and filament profiles, instead of requiring a pre-sliced file uploaded from an external slicer.
- Needs no new profile data — it consumes exactly the printer profile (Phase 4) and filament profile (Phase 5) data already being modeled.
- Generates the same sliced-file records defined in Phase 8 — Sliced files automatically, rather than requiring manual upload — no restructuring of the Sliced files or Jobs model needed when this lands.

## Phase 26 — Camera recording & timelapse

- A minimal *live* camera view already ships in Phase 1 (M0.7) — this phase covers what that doesn't: **optionally recording** and linking timelapses/snapshots back to the job that produced them.
- Connection details reuse the same flexible per-printer structure set up in Phase 1 — Fleet management + live Bambu integration.

## Out of scope for now

- Native mobile app (the webapp will be responsive-first).

## Open decisions

- License model for eventual public release (permissive vs. source-available/open-core) — worth deciding before the first public commit.
- Whether self-hosting or hosted SaaS is the priority distribution model post-MVP.
- Bambu Lab's LAN-only + Developer Mode requirement disables that printer's connection to Bambu Cloud/Handy while active — decide how clearly to surface this trade-off to users before they connect a printer. This is a real, immediate trade-off for Phase 1's own development, not just a future user-facing concern.
- Sourcing default printer/filament profile data: confirm license terms before bundling any third-party slicer profile data as defaults; the import-your-own-file approach avoids this question entirely and may be the safer default path.
- Retention/aggregation strategy for the printer status-history log (Phase 1) once it's been running for a while — raw timestamped events are fine to start, but worth revisiting before the table grows large, and before Phase 19 — Analytics dashboard leans on it heavily.
- Key management for connection-secret encryption: an install-generated key is enough for self-hosted, but the future hosted multi-tenant version (Phase 20) should move to a proper secrets-manager/KMS-backed key, likely per-tenant — worth deciding before the first hosted deployment, not urgent for MVP.
- Whether jobs (particularly unlimited-quantity ones) should ever get a per-job toggle to track a part's latest sliced file live instead of the one they locked onto at queue time — default stays snapshot either way; this is only about whether an opt-in live mode gets built at all, and isn't needed for MVP.
- Local connector agent's transport, machine-credential format, and pairing/rotation flow (how a customer installs and authorizes an agent for their org) aren't decided — not needed for MVP, but the printer connector should still be built as a swappable boundary in Phase 1 — Fleet management + live Bambu integration so this doesn't force a rewrite later.
- Bambu's local MQTT/Developer Mode access is unofficial-but-permitted, not a versioned/guaranteed API — firmware updates have previously narrowed third-party access. Worth an explicit maintenance-risk note for `BambuLanConnector`, not a one-time build-it-and-forget item.
- Exact submodule source and path for the reverse-engineered Bambu API reference material (Phase 1, M0.0) — placeholder pending the user providing the repo URL(s) before implementation.
