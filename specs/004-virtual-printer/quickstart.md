# Quickstart: Virtual Printer (Simulated Printer for UI Testing)

**Supersedes**: an earlier version of this guide that registered a simulated printer via a reserved
`192.0.2.0/24` IP address. The 2026-07-17 clarification follow-up replaced that with a real Brand/Model
catalog selection — this revision reflects the new flow only.

Validates the feature end-to-end once implemented, organized by the spec's three user stories. See
`data-model.md` for the simulated-state shapes and `contracts/IPrinterConnectorFactory.md` for the
interfaces referenced below.

## Prerequisites

- `api/` running locally against Postgres (`cd infra && docker compose up`, or `dotnet watch` per
  `CLAUDE.md`'s Local full stack instructions).
- `web/` running (`cd web && bun run dev`) for US1/US3 (browser-verifiable); US2 is verified via the
  backend test suite instead (see below — no live UI trigger exists yet, research.md §6).
- The Orval-generated API client has been regenerated after `VerifyPrinterConnectionRequest` gained
  `Brand`/`ModelCode` (research.md §4) — otherwise the dialog's verify call won't compile/match.
- No physical printer or Bambu hardware needed for any step in this guide.

## User Story 1 — Register a simulated printer through the normal flow

1. Open the product UI, go to Printers, click **Add printer** — the same dialog used for real hardware.
2. Fill in: any `Name`; select Brand **System**; the Model field auto-selects/shows **Simulation** (the
   catalog's only System-brand entry); any non-empty `IP address` (no reserved-range requirement
   anymore — any well-formed placeholder works, e.g. `10.0.0.1`); any non-empty `Serial number`; any
   non-empty `Access code` (anything other than the literal string `wrong`, which is reserved for a
   verify-failure test path — research.md §6).
3. Confirm the connection verifies (submit button enables) and click **Add**.
4. **Expected**: the printer appears in the fleet list within moments, status `Idle`, reachable, with
   Brand/Model showing **System / Simulation** — visibly identifiable as simulated (2026-07-17
   clarification), while every other panel/action behaves exactly like a real printer's (SC-001).
5. Register a second printer with Brand **Bambu** (a real or fixture-simulated address) and confirm both
   coexist in the fleet simultaneously, independently manageable (SC-005).

## User Story 3 — AMS and camera panels populate

1. Open the simulated printer's detail view (any System/Simulation printer — the catalog entry always
   carries `CameraTransport.BinaryImage`, so camera fidelity no longer depends on picking a particular
   model, unlike the Bambu brand's X1-family exclusion).
2. **Expected**: the AMS panel renders one unit with four slots showing varied material/color/remaining%
   (data-model.md's fixture table) — not empty. The camera panel renders a live-looking placeholder image
   rather than its "unavailable" state (SC-002).

## User Story 2 — Job lifecycle (backend-verified, no live UI trigger yet)

No product action exists yet to "send a print job to a printer" — `web/src/features/jobs` is a
client-only mock with no backend command (research.md §6, carried forward from the original pass). This
user story's state-machine behavior is therefore validated at the backend level, not by clicking
something in the running browser:

1. Run the migrated Infrastructure/integration suite:
   ```
   dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~Simulator"
   ```
2. **Expected assertions** (inside those tests, driving `SimulatorPrinterConnector.StartSimulatedJob`
   directly against a printer registered with Brand `System`/ModelCode `Simulation`):
   - Calling `StartSimulatedJob` on an Idle, connected simulated printer causes its `Printer.Status` to
     transition to `Printing`, broadcast via `IPrinterEventPublisher.PrinterStatusChangedAsync` — the same
     path a real Bambu `gcode_state` change would use (SC-003's "within ~2s" applies to this same
     publish/SignalR path).
   - `PrintProgressPercent` and nozzle/bed telemetry advance over the fixed cycle
     (`SimulatedPrinterState.CycleDuration`), reaching `AwaitingClear` within ~30-60s of wall-clock time
     in production, or near-instantly in this test suite (short `cycleDuration`) — confirms SC-007.
   - The printer does **not** free-run: with no `StartSimulatedJob` call, a connected simulated printer
     stays `Idle` indefinitely.
3. Once a real job-dispatch feature exists in a later phase (out of scope here), re-run this same
   validation by sending an actual job through the product UI instead of calling `StartSimulatedJob`
   directly — no simulator code change should be required to make that work.

## Mark ready (works identically to a real printer)

1. With a simulated printer showing `Awaiting Clear` (reached via the backend test path above, or —
   once job-dispatch exists — via the live UI), click **Mark ready** in the normal product UI.
2. **Expected**: the printer returns to `Idle` through the exact same `MarkPrinterReadyEndpoint` /
   `MarkPrinterReadyCommandHandler` path a real printer uses — no simulator-specific code on this path at
   all (FR-008).

## Regression check — a real Bambu-brand printer still uses the real connector

1. Confirm no `192.0.2.0/24`/`SimulatedPrinterAddressRange` reference remains anywhere in `api/src`
   (research.md §5 — the IP-range convention was retired outright, not left as a fallback).
2. Register a printer with Brand **Bambu** against actual or fixture-simulated Bambu hardware and confirm
   it still connects via `BambuLanConnector` exactly as before, regardless of which IP address it uses —
   its behavior must be provably unaffected by the selection mechanism change (SC-005).

## Architecture review checks (SC-004, SC-006)

- `grep -ri "virtual\|simulat" api/src/OpenFarm.Domain api/src/OpenFarm.Application web/src` should match
  **only** the ordinary, generic-looking `"System"`/`"Simulation"` Brand/ModelCode catalog values and any
  comments explaining why the seam is generic — no *branch*, *flag*, or *special-cased behavior* keyed on
  those values may exist above the connector boundary (2026-07-17 clarification: the catalog entry's
  existence is intentionally visible; its *behavior* is not).
- Diff review: confirm production changes are confined to one Domain catalog entry
  (`PrinterBrand`/`PrinterModelCatalog`), the `PrinterConnectionDetails` field addition and its call
  sites, `PrinterConnectorFactory`'s routing rule (Infrastructure), the `VerifyPrinterConnectionRequest`
  shape change (Api), and `AddPrinterDialog.tsx`'s Brand selector (web) — nothing else in `OpenFarm.Domain`
  beyond the one catalog entry, no new endpoint.
