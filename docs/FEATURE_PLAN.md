# OpenFarm — Feature Plan

## Overview

OpenFarm is a print farm management webapp covering printers, filament, maintenance, parts, assemblies (BOMs), and print jobs. It's being built open-source-first, kept private for now, and architected to support multi-tenancy from day one in case of future monetization.

## Guiding principles

- Usable by a solo operator or small farm without multi-tenant/SaaS complexity getting in the way.
- Automation is *suggested*, not forced — a human still physically loads spools and parts, so the system should assist decisions rather than take them over.
- Every feature should work for a self-hosted single-farm install and a future hosted multi-tenant version without redesign.

## Phase 0 — Foundation (prerequisite, not user-facing)

- Account structure ready for multiple organizations, even though self-hosted installs default to a single one automatically.
- Basic roles: Owner, Admin, Operator, Viewer. Operator and Viewer can optionally be scoped to specific printer tags/zones (reusing the tagging system from fleet management) rather than the whole farm; Owner and Admin stay farm-wide.
- Connection secrets (Bambu access codes today, other brands' credentials later) are encrypted at rest from day one using a server-side key — never stored in plaintext, so a raw database dump doesn't expose them.
- A generic, polymorphic activity log (entity, event type, actor, timestamp) underlies every status change, job update, and profile edit. This is what Phase 1's status-change logging and Phase 2's analytics both read from, and it doubles as an audit trail without being built twice.

## Phase 1 — MVP

Build order matters here. Printers and filament spools can be added and tracked without a profile attached — a profile only becomes required once you try to actually send a job to print. That lets fleet and filament tracking come online early, with profiles layered in afterward.

### 1. Fleet management (printers)
- Add, edit, and remove printers; track status (idle, printing, awaiting clear, maintenance, offline). No profile needed yet at this stage.
- Configure how each printer receives filament: single spool, AMS, auto-reload/splicer, or a combination (e.g. AMS plus a single-spool bypass, or AMS plus an auto-reload bypass).
- Tag printers (location, capability, farm zone, etc.) — the same tags double as scoping for Operator/Viewer roles (see Phase 0) and, later, circuit grouping (see Intelligent queue matching).
- A finished, failed, or cancelled job leaves its printer in **awaiting clear**, not idle — the bed still has a part on it. A quick, explicit "Mark ready" action (the same low-effort philosophy as the filament "changed" state below) moves it to idle, which is the only state a printer can be assigned a new job from, or be picked up by a queue-matching suggestion.
- Every status change, including time spent in awaiting clear, is logged with a timestamp via the shared activity log (see Phase 0) — this is what utilization, idle-time, and turnover-time analytics will be computed from later, so it needs to start accumulating from day one rather than being bolted on retroactively.
- Since Bambu Lab connection details (IP, access code, etc.) are being stored per-printer here anyway, keep that as a flexible, per-brand structure rather than fixed Bambu-only fields — Phase 2's other brands and Phase 3's camera feed will need to store their own connection details the same way, and this is the natural place for them to slot in later.

### 2. Filament tracking
- Spool tracking never requires a physical barcode/unique ID — an optional identifier can be added later for anyone who wants to scan spools, but it's never required to use the feature.
- Each spool has three states: **in storage** (not loaded), **using** (loaded onto a feed slot, actively tracked), and **finished** (depleted). Remaining weight is visible at the moment a spool is removed, so the operator can decide immediately whether it goes back to storage (still has usable filament) or gets marked finished — no in-between holding state needed.
- Remaining weight is estimated automatically from job usage while a spool is in use.
- For single-spool and AMS slots: marking a spool finished stays a manual action. A slot running out doesn't auto-continue, so the operator is already there physically replacing it — and pulling a spool doesn't always mean it's empty (could be a deliberate color change).
- For auto-reload slots: running out is exactly the moment the device is built to react to, so the system can detect it directly from the numbers — once cumulative job usage catches up to a spool's starting weight, mark it finished and flip the active slot to the standby spool automatically, without waiting on a manual mark. This is still an estimate rather than a precise measurement, so a manual override should stay available in case actual usage drifts from it over time.
- Before flipping to the standby spool, the system checks that its assigned filament profile matches the job's required material and color. A mismatch doesn't pause the swap — the entire point of auto-reload is continuing unattended — but it raises a dashboard warning and, once notifications exist (see Phase 2), an alert, and the mismatch is logged against the job so it's traceable afterward rather than silent.
- Internally modeled with a schema deliberately close to Spoolman's (spool / filament / vendor), even though this data lives natively in OpenFarm rather than depending on a separate Spoolman instance — see Phase 2's Spoolman-compatible filament API for why.

### 3. Live printer integration — Bambu Lab only
- Connect Bambu Lab printers over the local network for live status and AMS slot visibility (material/color currently loaded) — works as soon as a printer exists, independent of profiles.
- Full remote control requires the printer to be in LAN-only mode with Developer Mode enabled, per Bambu's current access model; printers left in Cloud mode only offer limited monitoring — worth surfacing this clearly to users when they connect a printer.
- Other brands (Prusa, Creality, generic OctoPrint/Klipper) stay manual-only for now; broader, brand-agnostic integration comes in Phase 2.
- This live connection assumes OpenFarm itself is on the same local network as the printers, which holds for the self-hosted deployment model. A future hosted SaaS deployment runs in the cloud and can't reach into a customer's home/office LAN directly — it will need a local connector agent on the customer's network instead (see Phase 3's Local connector agent). The printer connector is architected internally as a swappable boundary from the start so this doesn't require reworking Phase 1's implementation later.

### 4. Printer profiles
- Reusable spec template per printer model (e.g. "Bambu Lab X1 Carbon", "Prusa MK4"): nozzle size, bed volume, enclosure, supported materials, whether the model supports live integration.
- A printer must have a profile assigned before it can be sent a job — assignment happens after the printer already exists in the fleet.
- Ship a small set of generic starter profiles; also let users import their own profile data from their existing slicer setup rather than depending on us to source and maintain brand-specific values ourselves.

### 5. Filament profiles
- Reusable spec template per material, brand, and — where it actually matters — color (e.g. "Bambu PLA Basic – Black" can have its own tuned settings rather than sharing one generic profile across every color of that line). A color-specific profile can inherit from a shared base profile and only override what's different.
- A spool must have a profile assigned before it can be used in a print job.
- Same starter-set-plus-import approach as printer profiles.
- For connected Bambu printers, live AMS slot contents can be cross-checked against the filament profile catalog once this exists.

### 6. Part library
- Add parts with file, thumbnail, material profile, estimated print time, and estimated filament weight.
- The uploaded file is treated as the reusable source model (STL/3MF) rather than a pre-sliced, printer-specific output — worth being explicit about now so in-app slicing can be added in Phase 3 without reinterpreting existing parts.
- Simple revision tracking. Revising a part's source model flags its existing sliced files (see Sliced files) as potentially stale rather than invalidating them outright — a re-slice or re-upload is prompted, not forced immediately.
- Tag parts (category, product line, etc.).

### 7. Sliced files
- A part needs at least one **sliced file** (G-code, 3MF, or brand-specific bundle) before a job targeting it can be sent to a printer — distinct from the reusable source model in the Part library, and from printer/filament profiles.
- Each sliced file is tied to a specific printer profile (slicer settings depend on nozzle, bed, and material) and, where relevant, a filament profile (a Bambu multi-color 3MF bakes in AMS slot-to-color mapping). Any printer sharing that profile can use the same sliced file.
- A part can have multiple sliced files, one per printer profile it needs to run on — mirrors how a production run splits across different printer models in practice.
- Estimated time and filament weight are parsed from the file where possible, otherwise fall back to the manual estimates already on the Part.
- Phase 3's in-app slicing generates these same records automatically from the source model instead of requiring manual upload — no restructuring needed when that lands.

### 8. Assembly (BOM)
- Build assemblies from multiple parts plus quantities.
- Support nested assemblies (an assembly can include other assemblies as sub-components).

### 9. Jobs / print queue
- Create jobs targeting a part or assembly, with quantity and priority. Quantity can also be **unlimited** — a standing job that keeps producing rather than completing once a target count is hit, e.g. an evergreen part a printer falls back to whenever otherwise idle. A job targeting an assembly decomposes into part-level sub-jobs following the BOM quantities, recursively through nested assemblies — each component part needs its own matching sliced file for whichever printer it ends up on.
- A job — unlimited or not — locks onto a specific sliced file when it's queued, rather than dynamically following whatever the part's current sliced file is. If the part gets revised while the job is sitting in the queue or still running, the job keeps using the sliced file it already has (consistent with sliced files being flagged stale, not replaced, on revision — see Part library); it won't silently swap revisions mid-batch. A per-job option to instead always track the part's latest sliced file — most useful for unlimited jobs meant to run indefinitely — is a reasonable future addition, but isn't everyone's default, so it's deferred rather than built now (see Open decisions).
- Sending a job to a printer requires the printer's profile, the assigned spool's profile, and a sliced file matching that printer's profile (see Sliced files) to all be in place; the job can still be queued beforehand, and a queued job missing a sliced file is flagged clearly ("missing sliced file for [profile]") rather than blocking job creation itself.
- Manual status workflow: queued → printing → paused → failed → completed → cancelled. Reaching completed, failed, or cancelled moves the printer to **awaiting clear** rather than idle (see Fleet management) — the queue only offers a printer its next job once it's back to idle.
- Unlimited queue depth that stays fast to browse as it grows.
- Each job has a required material/color, defaulted from the part but overridable per job.
- For connected Bambu printers, live integration can auto-detect job start/finish once this feature exists, and sending a job pushes the matched sliced file to the printer directly; other brands require the operator to retrieve the sliced file (e.g. via USB/SD) and start the print manually, then mark it in progress — Phase 2's expanded live integration extends automatic push and detection to those brands too.
- Filament consumed by a job is recorded as a breakdown per spool/profile rather than a single total — a multi-color AMS job might draw from three different spools, and usage analytics later need to attribute grams to the right material and color, not just to the job as a whole.

### 10. Maintenance tracking
- Maintenance task templates triggered by calendar interval, print hours, or job count.
- Manual maintenance logs: performed by, date, notes, cost, parts replaced.
- Alerts when a printer crosses a maintenance threshold.

### 11. Dashboard
- At-a-glance view: printer statuses (including live Bambu status and printers awaiting clear), active jobs, low-filament alerts, maintenance due.

### 12. Backup & restore
- Full export of a single organization's farm data (printers, filament, profiles, parts, assemblies, jobs, maintenance history) as one downloadable file, triggerable from Settings by Owner or Admin.
- Restore from a previous export into a fresh install — covers both disaster recovery and moving to new hardware.
- Connection secrets are excluded from the default export and re-entered after a restore; an optional "include credentials" export mode encrypts them with a passphrase set at export time instead of shipping them in the clear.
- Exporting cleanly by organization, even before multi-tenancy is user-facing, is a useful forcing function for confirming the data model actually keeps tenants separable.

## Phase 2 — Smart features & integrations

### Intelligent queue matching
- Suggests which printer to assign a queued job to based on currently loaded filament (material + color match), to minimize spool swaps.
- Operator confirms suggestions rather than the system auto-claiming a machine.
- Only printers in the idle state (not awaiting clear) are considered candidates.
- When several suggested assignments dispatch at once, printer starts are staggered by a configurable delay rather than firing simultaneously, to avoid tripping a shared circuit. Printers can optionally be tagged into circuit groups (reusing the existing tag system) so staggering only applies within a shared circuit, not farm-wide.
- Later: factor in printer capability (from printer profiles — nozzle size, bed volume, enclosure) so impossible assignments are never suggested.
- Later: optimize across the whole queue at once, not just job by job.

### Optional spool identification
- Barcode/RFID scanning for anyone who wants faster, scan-based spool identification, layered on top of the same spool records — never required, since the default lifecycle already works without one.

### Spoolman-compatible filament API
- OpenFarm keeps owning its own filament data (needed for multi-tenancy and for linking spools to jobs, slots, and auto-reload logic), but exposes spool/filament/vendor endpoints matching Spoolman's REST API shape.
- This means Moonraker's built-in Spoolman integration (for Klipper-based printers), Home Assistant's Spoolman integration, and existing NFC hardware built against Spoolman (scanning scales, tag readers) work against OpenFarm without modification.
- A one-time import tool pulls vendors, filaments, and spools from an existing Spoolman instance, for farms migrating over rather than starting cold.

### Expanded live printer integration
- Extend live integration beyond Bambu Lab to other brands/protocols (OctoPrint, Klipper/Moonraker, Prusa Connect, etc.), bringing them up to the same auto-status-detection level Bambu Lab got in Phase 1.

### Order integration
- Auto-create jobs from marketplace orders (Shopee, TikTok Shop, Lazada).
- Link jobs back to the originating order for fulfillment tracking.

### Notifications
- Telegram, Discord, or email alerts for maintenance due, job completed, or low filament.

### Analytics dashboard
- Configurable, card-based layout — users choose which metrics to show rather than a fixed set.
- Metrics available as cards: filament usage (by material, color, printer, or time period), printer utilization rate, total print time, idle time, cost per part/assembly/job.
- Optional selling price per part/assembly, with margin (price minus estimated cost) surfaced next to the existing cost metrics. Estimated cost draws from filament cost-per-gram (from spool purchase price) and a configurable per-printer hourly machine rate, giving a quick quote for a given part and quantity without needing full order integration.
- Because the underlying data (printer status history, per-job filament breakdown) has been captured since Phase 1, this is mostly aggregation and UI work rather than a data model change.

## Phase 3 — Scale & monetization

- Full multi-tenant SaaS: organization switching, team invites, billing.
- Public API / webhooks for community integrations.
- Predictive maintenance based on usage patterns.
- Plugin/extension ecosystem for the open-source community.

### Local connector agent
- Required before hosted SaaS can offer live printer integration at all — self-hosted installs never need this, since OpenFarm already runs on the farm's own network there.
- A lightweight agent installed on the customer's local network (one per farm/site) connects outward to the OpenFarm cloud backend, avoiding any inbound firewall/port-forwarding setup on the customer's side, and relays printer connectivity (Bambu LAN today, other brands as Phase 2 expands live integration) the same way the self-hosted deployment does directly.
- Authenticates as a machine identity scoped to the organization, distinct from a user login, with a narrow permission surface (report telemetry, receive commands) rather than the full user role/permission set.
- Reuses the same printer-connector boundary built in Phase 1's live integration work — only the transport (direct LAN vs. relayed through the agent) differs, not the per-brand connection logic itself, so this doesn't require rebuilding Phase 1/2's printer integration.

### In-app slicing
- Slice a part's source model directly in OpenFarm using its assigned printer and filament profiles, instead of requiring a pre-sliced file uploaded from an external slicer.
- Needs no new profile data — it consumes exactly the printer profile and filament profile data already being modeled from Phase 1 onward.
- Generates the same sliced-file records defined in Phase 1 automatically, rather than requiring manual upload — no restructuring of the Sliced files or Jobs model needed when this lands.

### Camera / live monitoring
- View a printer's live camera feed (where supported) directly from OpenFarm.
- Optionally record and link timelapses/snapshots back to the job that produced them.
- Connection details reuse the same flexible per-printer structure set up for live integration in Phase 1.

## Out of scope for now

- Native mobile app (the webapp will be responsive-first).

## Open decisions

- License model for eventual public release (permissive vs. source-available/open-core) — worth deciding before the first public commit.
- Whether self-hosting or hosted SaaS is the priority distribution model post-MVP.
- Bambu Lab's LAN-only + Developer Mode requirement disables that printer's connection to Bambu Cloud/Handy while active — decide how clearly to surface this trade-off to users before they connect a printer.
- Sourcing default printer/filament profile data: confirm license terms before bundling any third-party slicer profile data as defaults; the import-your-own-file approach avoids this question entirely and may be the safer default path.
- Retention/aggregation strategy for the printer status-history log once it's been running for a while — raw timestamped events are fine to start, but worth revisiting before the table grows large.
- Key management for connection-secret encryption: an install-generated key is enough for self-hosted, but the future hosted multi-tenant version should move to a proper secrets-manager/KMS-backed key, likely per-tenant — worth deciding before the first hosted deployment, not urgent for MVP.
- Whether jobs (particularly unlimited-quantity ones) should ever get a per-job toggle to track a part's latest sliced file live instead of the one they locked onto at queue time — default stays snapshot either way; this is only about whether an opt-in live mode gets built at all, and isn't needed for MVP.
- Local connector agent's transport, machine-credential format, and pairing/rotation flow (how a customer installs and authorizes an agent for their org) aren't decided — not needed for MVP, but the printer connector should still be built as a swappable boundary in Phase 1 so this doesn't force a rewrite later.
