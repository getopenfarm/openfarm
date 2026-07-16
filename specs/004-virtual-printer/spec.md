# Feature Specification: Virtual Printer (Simulated Printer for UI Testing)

**Feature Branch**: `claude/virtual-printer-implementation-f7zzae`

**Created**: 2026-07-16

**Status**: Draft

**Input**: User description: "I want to implement virtual printer, and remove the use fake connector config in backend. So that if we wanna test out UI we can just add a virtual printer, we can simulate on that."

## Clarifications

### Session 2026-07-16

- Q: How far should this turn go? → A: Author the specification only; planning and implementation follow separately.
- Q: How is the simulated printer driven? → A: By built-in default behaviour, not a control surface. It behaves like a real machine — idle and reachable with baseline readings until a print job is sent to it, then it drives a realistic print lifecycle (Printing → advancing progress → Awaiting Clear) in response, exactly as real hardware would. No separate developer/manual control surface is built in this phase.
- Q: How much of a real printer should it simulate? → A: Full fidelity — telemetry, AMS, and camera all populate (from sensible defaults), so every product panel is exercised.
- Q: Should "virtual" be a printer type the frontend and backend know about? → A: No. The frontend and the Domain/Application layers must not differentiate a simulated printer from a real one at all — a printer is a printer. Only the connector (Infrastructure) layer is aware. Treat a simulated printer exactly like a real machine.
- Q: Do we need a developer control surface to drive the simulation this phase? → A: No. Default behaviour reacting to normal product actions (notably a print job being sent to the printer) is sufficient; the product screens simply show the printer reacting, identically to real hardware.
- Q: How much should the connector abstraction be generalized for future brands? → A: Keep the connector seam brand-neutral so adding a connector (the simulator now, other brands later) is a new implementation with no changes to core code; generalize only as far as the simulator needs now, not speculatively.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register a simulated printer through the normal flow (Priority: P1)

A developer or tester wants to exercise the OpenFarm UI without any physical hardware. They register a printer using the standard Add-printer flow — the same screen, the same fields, the same verify-before-add gate a real printer uses — pointed at the built-in simulator. The printer verifies, connects, and appears in the fleet list showing a live status, exactly like a real machine. Nothing anywhere on screen indicates the printer is simulated; it is simply a printer in the fleet.

**Why this priority**: This is the foundational capability — without being able to get a working, connected printer into the fleet with no hardware, none of the UI (dashboard, detail, telemetry, AMS, camera) can be exercised. It also proves the core architectural goal: a simulated printer is indistinguishable from real hardware everywhere above the connector layer.

**Independent Test**: Can be fully tested by registering one simulated printer through the normal Add-printer flow on a machine with no physical printer, and confirming it appears in the fleet list with a live status — delivers standalone value as "I can test the fleet UI without hardware."

**Acceptance Scenarios**:

1. **Given** a developer on a machine with no physical printer, **When** they register a printer through the standard Add-printer flow pointed at the simulator, **Then** the connection verifies, the confirm/add action enables, and the printer appears in the fleet list within moments showing a live status.
2. **Given** a registered simulated printer, **When** a developer inspects the product fleet and detail screens, **Then** there is no visible indication that the printer is simulated — it is presented identically to a real printer.
3. **Given** a registered simulated printer, **When** a developer also has a real printer registered, **Then** both coexist in the same fleet at the same time and are managed through the same screens.

---

### User Story 2 - Drive the simulated printer's state to test the UI (Priority: P2)

A tester wants the product UI to show real printer states without hardware. They use the normal product UI: a connected simulated printer sits Idle and reachable with baseline readings, and when a print job is sent to it (the same action taken against a real printer), it behaves like a real machine — it transitions to Printing, advances its progress and nozzle/bed temperatures over time, and ends in Awaiting Clear when the job completes. The product dashboard, status badges, printer-detail view, and the "Mark ready" flow all react in real time, exactly as they would for real hardware. The behaviour is built into the simulator; there is no separate control surface to operate.

**Why this priority**: A printer that reacts to normal product actions like a real machine is what makes the fleet, status, and job flows testable without hardware, and it keeps the product free of any simulation-specific UI. It depends on US1 (a printer must exist first).

**Independent Test**: Can be fully tested by sending a print job to a connected simulated printer through the normal UI and confirming the printer moves through the print lifecycle in the product UI in real time, without a page refresh.

**Acceptance Scenarios**:

1. **Given** a connected simulated printer showing Idle, **When** a print job is sent to it through the normal product UI, **Then** the product UI shows the printer transition to Printing with advancing progress, without a page refresh.
2. **Given** a simulated printer that is Printing, **When** its simulated job completes, **Then** the product UI shows it as Awaiting Clear rather than automatically returning to Idle.
3. **Given** a simulated printer showing Awaiting Clear, **When** the operator uses "Mark ready" in the normal product UI, **Then** the printer returns to Idle exactly as a real printer would.
4. **Given** a connected simulated printer with no job sent, **When** it is left alone, **Then** it stays Idle and reachable with baseline readings and does not start printing on its own (it reacts to jobs, it does not free-run).

---

### User Story 3 - Populate AMS and camera panels (Priority: P3)

A tester wants the richer printer-detail panels to render without hardware. A connected simulated printer reports sensible default AMS contents — units, slots, and each slot's material, color, and remaining percentage — and produces a camera image on its own, just as a real printer reports these once connected. The product's AMS slot grid and camera view render with full fidelity.

**Why this priority**: Telemetry and status (US2) cover most of the UI, but AMS and camera are distinct panels that also need to be testable without hardware. Lower priority because the core fleet and status experience is already testable after US2.

**Independent Test**: Can be fully tested by opening a connected simulated printer's detail view and confirming the AMS and camera panels render its default contents.

**Acceptance Scenarios**:

1. **Given** a connected simulated printer, **When** its detail view is opened, **Then** the product AMS panel renders default slots with their material, color, and remaining percentage.
2. **Given** a connected simulated printer, **When** its detail view is opened, **Then** the product camera view renders a live-looking feed rather than its unavailable state.

---

### Edge Cases

- What happens when the global fake-connector configuration is removed and an install has no physical hardware? Real printers connect for real by default; testing the UI without hardware is done by registering a simulated printer, not by a hidden global mode.
- What happens when a simulated printer and a real printer are registered at the same time? Both must work independently; neither disables or interferes with the other (this was impossible under the old global switch).
- How does the system handle the "Mark ready" and removal actions on a simulated printer? Identically to a real printer — they are the same product flows.
- What happens if someone inspects the product UI or Domain/Application code looking for "virtual" handling? They find none; the concept exists only in the connector layer.
- How does the connector layer decide a given printer should be simulated without the layers above knowing? By a convention it alone interprets from the connection details it is handed (the specific convention is an implementation detail, invisible to the user and to Domain/Application).
- What happens to a simulated printer's state across an application restart? Simulated state is not durable; after a restart the printer reconnects and its default readings are re-established, and it is Idle again (same as a real printer re-reporting after reconnect).
- What happens if a print job is sent to a simulated printer before the job-dispatch flow exists in the product? Until that flow exists, the simulated printer simply stays Idle and reachable with baseline readings; the print-lifecycle behaviour engages once print jobs can be dispatched to printers.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a simulated printer that can be registered and operated through the exact same flow, fields, and screens as a real printer.
- **FR-002**: The concept of a "virtual" or "simulated" printer MUST NOT appear in the product UI or in the Domain/Application layers — no printer type field, no selector, no virtual-specific branch. A printer is a printer above the connector boundary.
- **FR-003**: The distinction between a simulated printer and real hardware MUST be confined entirely to the connector (Infrastructure) layer; that layer alone determines whether a printer is served by the simulator or a real connector.
- **FR-004**: A simulated printer MUST appear in the fleet and detail views indistinguishably from a real printer — same panels, same status semantics, same actions.
- **FR-005**: The simulator MUST have built-in default behaviour that mirrors a real printer: while connected and with no job, it reports a stable Idle status with baseline readings; when a print job is sent to it, it transitions to Printing, advances print progress and nozzle/bed temperatures over time, and ends in Awaiting Clear when the job completes. It MUST NOT free-run or auto-cycle without a job.
- **FR-006**: A connected simulated printer MUST report default AMS contents (units, slots, and per-slot material, color, and remaining percentage) and MUST produce a camera image on its own, so those product panels render with full fidelity — no manual setup required.
- **FR-007**: The simulation MUST be driven by the simulator's built-in behaviour reacting to normal product actions (notably a print job being sent), NOT by any dedicated control surface — the product UI carries no awareness of simulation. Simulated state changes MUST propagate to all connected product clients in real time, exactly as real-printer updates do.
- **FR-008**: The "Mark ready" transition (Awaiting Clear → Idle) MUST work on a simulated printer through the normal product UI, identically to a real printer.
- **FR-009**: The system MUST remove the global fake-connector configuration switch; the choice of connector MUST be made per printer within the connector layer, not by a process-wide flag.
- **FR-010**: With the global switch removed, real printers MUST use the real connector by default, and a simulated printer MUST be able to coexist with real printers in the same fleet at the same time.
- **FR-011**: Adding a new connector implementation (the simulator now, another printer brand later) MUST require only a new implementation and its registration inside the connector layer — with NO changes to the Domain, Application, frontend, or connection-orchestration. The connector abstraction MUST carry no hardware- or brand-specific assumptions in the general flow.
- **FR-012**: The general flow MUST NOT assume every printer has a network address and access code; whether connection details are required, and which, MUST be owned by the connector layer so a simulated printer needs no real hardware credentials.
- **FR-013**: Each connector MUST translate its own protocol's status and telemetry vocabulary into the system's canonical printer status and telemetry model, so the shared Domain remains brand-neutral.
- **FR-014**: Removing a simulated printer MUST behave like removing a real one (it stops simulating and drops from the fleet), and simulated printers MUST be tenant-scoped and permission-gated identically to real printers.

### Key Entities *(include if feature involves data)*

- **Printer**: A managed printer in the fleet. It is generic — it carries no attribute distinguishing simulated from real. Above the connector boundary, every part of the system treats it identically.
- **Printer connector**: The uniform, brand-neutral boundary every printer flows through (connection verification, a live event stream, and camera frames). The real hardware connector and the simulator are two implementations of it; future brands are additional implementations, selected the same way, entirely within the connector layer.
- **Printer simulator**: A connector implementation with built-in default behaviour that produces full-fidelity telemetry, status, AMS, and camera output, feeding the same event stream a real connector produces. It reacts to normal product actions (a print job being sent) the way real hardware would, with no external control surface.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A tester registers a working simulated printer through the standard Add-printer flow and sees it in the fleet in under 1 minute, using the same screen a real printer uses.
- **SC-002**: Every printer-detail panel (status, telemetry, AMS, camera) populates on a connected simulated printer through its built-in default behaviour, with no physical hardware and no manual setup.
- **SC-003**: When a print job is sent to a simulated printer, the resulting state change is reflected in the product UI within about 2 seconds, with no page refresh.
- **SC-004**: A review of the product UI and the Domain/Application code finds zero references to "virtual" or "simulated" printer handling — the concept is present only in the connector layer.
- **SC-005**: A simulated printer and a real printer run in the same fleet simultaneously, and removing the global switch causes zero real printers to silently stop connecting.
- **SC-006**: Adding the simulator required no changes to the Domain, Application, frontend, or connection-orchestration beyond a new connector implementation — demonstrating that a future printer brand can be added the same way.

## Assumptions

- The simulated printer is a testing, demo, and development facility shipped with the application; it is not a customer-facing production feature.
- No dedicated developer or manual control surface is built this phase; the simulator's built-in default behaviour, reacting to normal product actions (notably a print job being sent), is sufficient. Richer manual control could be added later but is out of scope here.
- The full print-lifecycle behaviour depends on the ability to send/dispatch a print job to a printer; that flow may belong to the print-jobs feature and may not exist yet. Until it does, a simulated printer behaves as an Idle, reachable printer with baseline telemetry, AMS, and camera output.
- How the connector layer recognizes which printers it should simulate (for example, a reserved address or convention in the connection details it is handed) is an implementation decision, invisible to the user and to the Domain/Application layers.
- Camera simulation uses a synthetic or placeholder image rather than a real video stream.
- Existing permissions gate registering and removing printers; the simulator introduces no new end-user role.
- The connector boundary is made brand-neutral only as far as the simulator requires now; the specific connection-parameter shapes of other printer brands are built out when those brands are actually added, not speculatively in this feature.
- Bambu Lab LAN remains the only real connector that exists today (from Phase 1); this feature adds the simulator alongside it and removes the global connector switch.
