# Feature Specification: Phase 1 — Fleet Management + Live Bambu Integration

**Feature Branch**: `002-printer-integration`

**Created**: 2026-07-12

**Status**: Draft

**Input**: User description: "Specify phase 1 (printer integration), for bambulab API please add submodule for reference purpose (https://github.com/tanghq33/bambulab-api-docs/tree/develop), since there is no official API docs so i gathered and store it in a git repo, make sure store in a a structure that it can fit other API, in future we might integrate more printer brand."

## Clarifications

### Session 2026-07-12

- Q: When an operator tries to add a printer whose serial number or IP address matches one already registered in the org, what should happen? → A: Block on serial number only; duplicate IP addresses are allowed.
- Q: When an operator edits an existing printer's connection details (IP address or LAN access code), should the same verify-before-save gate used when adding a printer apply? → A: Yes, save stays disabled until the edited connection details are verified reachable.
- Q: How long should the system wait without a successful response from a previously-online printer before marking it unreachable/offline? → A: ~30 seconds.
- Q: What should happen if an operator tries to remove a printer that currently has status Printing? → A: Allow, after a confirmation warning; removal is not blocked by print status.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Operator adds a printer and watches its live status (Priority: P1)

An operator registers a Bambu Lab printer on their local network with the app (name, model, IP address, serial number, and its LAN access code). Before the printer can be added, the app verifies it can actually reach and authenticate with the printer using those details — the confirm/add action stays disabled until that verification succeeds. Once added, the printer appears in the fleet list showing its current status — Idle, Printing, or Awaiting Clear — and that status updates on its own as the printer's real-world state changes, with no need to refresh the page.

**Why this priority**: This is the foundational capability everything else in the print farm depends on. Without a printer registered and reporting real status, there is no fleet to manage, monitor, or eventually operate. Verifying the connection up front is what makes that "reporting real status" promise trustworthy from the very first printer added.

**Independent Test**: Can be fully tested by adding one physical (or simulated) printer, changing its state (e.g. starting a print from the printer itself), and confirming the fleet list reflects the new status without any manual refresh — delivers standalone value as "I can see what my printer is doing right now."

**Acceptance Scenarios**:

1. **Given** an operator with a Bambu printer on their LAN, **When** they enter valid connection details, **Then** the app verifies the connection and enables the confirm/add action, and the printer appears in the fleet list within moments, showing Idle status.
2. **Given** an operator enters an unreachable IP address or an incorrect LAN access code, **When** connection verification runs, **Then** the confirm/add action stays disabled and a clear, actionable error is shown, and no printer is added until valid details are provided.
3. **Given** a registered printer that is Idle, **When** the operator starts a print directly from the printer or its official app, **Then** the fleet list shows the printer's status change to Printing without the operator refreshing anything.
4. **Given** a print finishes, fails, or is cancelled, **When** the printer reports this, **Then** the printer's status becomes Awaiting Clear rather than automatically returning to Idle.

---

### User Story 2 - Operator monitors live telemetry and AMS slot contents (Priority: P1)

From a printer's detail page, an operator sees live telemetry — temperatures, print progress, and the material loaded in each AMS slot — updating continuously while a print is running, without needing to check the printer in person or refresh the browser.

**Why this priority**: Live telemetry is the core "why bother" of connecting printers at all — it's what turns a static inventory list into an actual fleet-monitoring tool, and is equally critical to the MVP as seeing status alone.

**Independent Test**: Can be fully tested by opening a printer's detail page during an active print and confirming temperature, progress percentage, and AMS slot contents change on-screen as the physical print progresses — delivers value as "I can watch my print job from my desk."

**Acceptance Scenarios**:

1. **Given** a printer is actively printing, **When** the operator opens its detail page, **Then** they see current nozzle/bed temperatures and print progress, updating live.
2. **Given** a printer with an AMS unit loaded with multiple filament colors/materials, **When** the operator views the detail page, **Then** each slot's contents are shown and reflect what is physically loaded.

---

### User Story 3 - Operator views a live camera feed (Priority: P2)

From the same printer detail page, the operator can see a live video feed from the printer's built-in camera, so they can visually confirm a print is progressing correctly without walking over to it.

**Why this priority**: High demo and day-to-day value, but the fleet is still fully usable via status and telemetry alone if the camera feed isn't available yet — hence P2, not P1.

**Independent Test**: Can be fully tested by opening a printer's detail page and confirming a live video stream renders and updates — delivers value as "I can see my printer, not just its numbers."

**Acceptance Scenarios**:

1. **Given** a printer that supports a local camera stream, **When** the operator opens its detail page, **Then** a live video feed is visible and updating.
2. **Given** a printer whose camera stream is temporarily unreachable, **When** the operator opens its detail page, **Then** the rest of the page (status, telemetry) still works normally and the camera area shows a clear unavailable state instead of breaking the page.

---

### User Story 4 - Operator clears a finished printer and every change is recorded (Priority: P2)

After a print completes, fails, or is cancelled, the printer sits in Awaiting Clear until the operator physically removes the print and explicitly marks it ready again. Every one of these transitions — manual or automatic — is recorded in the shared activity history so there's a traceable record of what happened and when.

**Why this priority**: Important for daily operational trust and for later phases (queueing, analytics) that depend on this same status history, but the fleet is demoable without it if status/telemetry (P1) already work — hence P2.

**Independent Test**: Can be fully tested by completing a print, confirming the printer shows Awaiting Clear, performing the "Mark ready" action, confirming it becomes Idle, and confirming both transitions appear in the activity history — delivers value as "I always know what happened to a printer and when."

**Acceptance Scenarios**:

1. **Given** a printer in Awaiting Clear, **When** the operator performs "Mark ready," **Then** the printer becomes Idle and this action is recorded in the activity history.
2. **Given** a printer in Idle, **When** an operator attempts to force it directly into Awaiting Clear (an illegal manual jump), **Then** the system rejects the action with a clear explanation.
3. **Given** any status change reported by the printer's connection (not manually triggered), **When** it occurs, **Then** it is also recorded in the activity history.

---

### User Story 5 - Operator distinguishes "offline" from print status (Priority: P3)

An operator can tell at a glance whether a printer is simply not printing (Idle) versus unreachable over the network (offline/disconnected) — these are shown as clearly separate pieces of information, not conflated into one status value.

**Why this priority**: Valuable for troubleshooting and trust in the data shown, but the fleet remains usable without this distinction being polished from day one — hence P3.

**Independent Test**: Can be fully tested by disconnecting a printer from the network and confirming the UI shows it as unreachable/offline while its last-known print status remains visible and unchanged — delivers value as "I can tell a network problem from a print problem."

**Acceptance Scenarios**:

1. **Given** a registered printer loses network connectivity, **When** the operator views the fleet list, **Then** the printer is clearly marked as offline/unreachable, distinct from its last-known Idle/Printing/Awaiting Clear status.
2. **Given** a printer regains connectivity, **When** it reconnects, **Then** the offline indicator clears and live status/telemetry resume.

### Edge Cases

- What happens when the printer's connection drops in the middle of an active print? (After approximately 30 seconds without a successful response, status reflects "unreachable," rather than silently reverting to Idle or freezing on stale data.)
- What happens when an operator attempts an illegal manual status transition (e.g. Idle directly to Awaiting Clear)? (Rejected with a clear message; no partial state change.)
- What happens when the printer's connection reports a status that conflicts with the last manual action — e.g. someone started a print directly from the printer's own screen or official app? (The system reconciles to the reported reality and logs the discrepancy, rather than rejecting it.)
- What happens when the operator enters an unreachable IP address or an incorrect LAN access code while adding a printer? (The confirm/add action stays disabled until the app successfully verifies the connection; a clear, actionable error explains why, so no printer is ever added in an already-broken state.)
- What happens when a printer's camera feed is on a port or protocol the app doesn't expect for that printer's generation? (Camera area shows a clear unavailable state; the rest of the printer detail page keeps working.)
- What happens when an operator adds a printer whose serial number matches an already-registered printer? (Rejected with a clear error identifying the conflicting printer; duplicate IP addresses across different printers are permitted, since IP reuse via DHCP is common.)
- What happens when an operator removes a printer that currently has status Printing? (A confirmation warning is shown, but removal is always permitted regardless of print status.)

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow an operator to register a printer with a name, model, IP address, serial number, and a securely stored connection credential.
- **FR-001a**: System MUST verify that a printer's connection details (IP address and LAN access code) are reachable and valid before the printer can be added, and MUST keep the confirm/add action disabled until that verification succeeds — a printer is never added in a state that is already known to be unreachable.
- **FR-001b**: System MUST reject an attempt to add a printer whose serial number matches an already-registered printer in the same organization, with a clear error identifying the conflicting printer; duplicate IP addresses across different printers are permitted.
- **FR-002**: System MUST allow an operator to view, edit, and remove a registered printer. Editing a printer's connection details (IP address or LAN access code) is gated by the same verify-before-save requirement as FR-001a — the save action stays disabled until the new details are verified reachable. Removal is never blocked by the printer's current print status (including Printing) — the operator sees a confirmation warning first, but removal is always permitted.
- **FR-003**: System MUST track each printer's status as one of Idle, Printing, or Awaiting Clear, and MUST NOT allow an illegal manual transition between these (e.g. skipping directly from Idle to Awaiting Clear) — any such attempt is rejected with a clear explanation.
- **FR-004**: System MUST accept and reconcile status changes reported by a printer's live connection even when they don't follow the manual-transition rules (e.g. a print started from the printer's own screen), rather than rejecting them.
- **FR-005**: System MUST track a printer's network connectivity (reachable / unreachable) separately from its print status, so the two are never conflated in what the operator sees. A printer is marked unreachable/offline after approximately 30 seconds without a successful response from its live connection, to avoid flickering the indicator on brief network blips.
- **FR-006**: System MUST store each printer's connection credential encrypted at rest, consistent with how connection secrets are already handled elsewhere in the system.
- **FR-007**: System MUST push status and telemetry updates to the operator's screen automatically, with no manual page refresh required.
- **FR-008**: System MUST display live telemetry (temperatures and print progress) and live AMS slot contents on a printer's detail page while that data is available.
- **FR-009**: System MUST display a live camera feed on a printer's detail page when the printer supports one, and MUST degrade gracefully (clear "unavailable" state, rest of the page unaffected) when it does not.
- **FR-010**: System MUST record every printer status change — manual or connector-reported — in the shared activity history, including who or what triggered it.
- **FR-011**: System MUST NOT provide any way to start, queue, or push a print job to a printer in this phase — fleet visibility and monitoring only; sending work to a printer is out of scope until a later phase.
- **FR-012**: System MUST maintain reference documentation for each supported printer brand's integration protocol as version-controlled reference material organized so that adding a new brand does not require restructuring the reference material for existing brands.

### Key Entities

- **Printer**: A registered piece of fleet hardware. Holds identifying/spec details (name, model, IP address, serial number) directly rather than referencing a separate template, plus a reference to its encrypted connection credential and its current status. Serial number is unique per organization; IP address is not (duplicates are permitted).
- **Printer Status**: The Idle / Printing / Awaiting Clear state of a printer, changed either through an explicit operator action (which enforces legal transitions) or through the printer's own reported reality (which reconciles rather than rejects). Network reachability is tracked as a separate piece of state, not a value of this status.
- **Connection Credential**: The sensitive secret (e.g. a LAN access code) needed to connect to a printer, stored encrypted and never shown back in full once saved — reuses the existing secure-credential handling already established in the system.
- **Live Telemetry**: Point-in-time data streamed from a printer while connected — temperatures, print progress, AMS slot contents. Reflects current state; not a historical record.
- **Activity History Entry**: A record of a single event — in this feature, a printer status change — capturing what changed, when, and whether it was operator-initiated or reported by the printer's connection.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can register a new printer and see it appear in the fleet list in under 2 minutes.
- **SC-002**: A status change on the physical printer is reflected in the fleet list and detail page within 5 seconds, with no manual refresh.
- **SC-003**: 100% of printer status changes, whether operator-initiated or printer-reported, appear in the activity history.
- **SC-004**: An illegal manual status transition is rejected 100% of the time, with a message an operator can understand without technical background.
- **SC-005**: When opening a printer's detail page during an active print, live camera video becomes visible within 3 seconds.
- **SC-006**: The fleet list remains responsive and shows accurate live status for at least 20 simultaneously connected printers.
- **SC-007**: 100% of attempts to add a printer with an unreachable IP address or incorrect LAN access code are blocked before the printer is added, with no unreachable printer ever appearing in the fleet list as a result.

## Assumptions

- The Bambu Lab LAN-only + Developer Mode requirement disconnects a printer from Bambu Cloud/the official app while the app is connected to it directly; this trade-off is surfaced to the operator as a clear, plain-language notice during printer setup, not hidden or buried.
- Printer status-history entries accumulate as raw timestamped events with no special retention or aggregation policy yet — acceptable for an initial fleet size, to be revisited once volume or analytics needs grow.
- This phase covers Bambu Lab X1/P1/A1-series printers only; other printer brands are explicitly out of scope and will extend the same reference-material and connection patterns established here in a later phase.
- A printer's spec fields (model, nozzle, bed size, etc.) are captured directly on the printer record rather than a separate reusable printer-profile — acceptable while an operator typically manages only one or two printers; extracting a shared profile is deferred until duplication across many printers makes it worthwhile.
- A dedicated "Maintenance" printer status is not yet available in this phase; it is deferred until a maintenance-tracking capability exists to make use of it.
- Validation of the live connection against physical hardware is a manual process performed by the development team, not an automated or continuously-running check.
- Reverse-engineered protocol reference material is sourced from the operator's own prior research (a community-style documentation repository) since Bambu Lab does not publish an official API; this material is treated as a living reference that may be updated over time, not a frozen snapshot.
