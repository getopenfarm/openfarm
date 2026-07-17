# Phase 1 Data Model: Virtual Printer (Simulated Printer for UI Testing)

**Supersedes**: an earlier version of this document built around IP-address-based connector selection.
The 2026-07-17 clarification follow-up replaced that with a Domain-catalog-based selection (a real
Brand/Model entry), so this revision adds one Domain catalog entry and changes the connection-details
shape; the in-memory simulator-state model itself (this feature's only non-persisted "entity") is
unaffected and carries forward unchanged.

## Domain catalog change (the one Domain-layer touch this feature makes)

### `PrinterBrand` (`OpenFarm.Domain.Printers`) — one new enum member

```csharp
public enum PrinterBrand
{
    Bambu,
    System,
}
```

### `PrinterModelCatalog` (`OpenFarm.Domain.Printers`) — one new entry

| Brand | ModelCode | CameraTransport |
|---|---|---|
| `System` | `"Simulation"` | `BinaryImage` |

This is the same additive, compile-time `(Brand, ModelCode) -> PrinterModel` lookup every other printer
model already lives in (`PrinterModelCatalog.cs`'s own doc comment: "adding a brand/model is a new entry
here, never a rewrite") — no new Domain type, no new validation path. `Printer.Create`'s existing
`PrinterModelCatalog.Resolve(brand, modelCode)` call resolves `(System, "Simulation")` exactly like any
other pair, with zero changes to `Printer.Create` itself.

**Why `CameraTransport.BinaryImage`, not a new transport**: `PrinterConnectionBackgroundService`'s
existing camera-loop gate (`CameraTransport == CameraTransport.BinaryImage`) triggers unmodified for the
Simulation model, giving every simulated printer full camera fidelity unconditionally — no new gate, no
simulation-specific branch (FR-002, FR-006).

## `Printer` (existing entity — no schema/column change)

Unaffected. A simulated printer's `Brand`/`ModelCode` columns simply hold the ordinary string values
`"System"`/`"Simulation"`, resolved and stored exactly like any real printer's — same columns, same
`SerialNumber`-uniqueness constraint, same `OrganizationId` tenant scoping. Nothing distinguishes a
simulated printer's row shape from a real one's; only its *values* do, visibly, by design (2026-07-17
clarification — Brand/Model are intentionally visible everywhere they already display today).

## Connection-details shape (Application-owned, one additive change)

```csharp
public sealed record PrinterConnectionDetails(string Brand, string ModelCode, string IpAddress, string SerialNumber, string AccessCode);
```

`Brand`/`ModelCode` are new fields (previously this record carried only `IpAddress`/`SerialNumber`/
`AccessCode`). Every construction site already has both values in scope before building this record
(see research.md §2 for the full call-site list), so this is a mechanical, additive change — not a new
dependency or new orchestration step. Real connectors (`BambuLanConnector`, `BambuCameraRelay`) ignore
`Brand`/`ModelCode` entirely; only `PrinterConnectorFactory`'s routing logic reads them.

The simulator is selected when:

```csharp
string.Equals(details.Brand, "System", StringComparison.OrdinalIgnoreCase)
    && string.Equals(details.ModelCode, "Simulation", StringComparison.OrdinalIgnoreCase)
```

This replaces the retired `SimulatedPrinterAddressRange` (`192.0.2.0/24` IP-range) check outright — see
research.md §5. IP address, serial number, and access code stay required, non-durable, non-special-cased
fields for a simulated printer exactly as for a real one (2026-07-17 clarification); the user supplies
placeholder values.

## In-memory simulator state (unchanged from the original design)

Everything below carries forward unchanged from the pre-reversal data-model.md — the simulator's own
*behavior* was never in question, only how a printer opts into it.

### `SimulatedPrinterState` (record/class, `OpenFarm.Infrastructure.Printers.Simulator`)

One instance per registered simulated printer, keyed by `SerialNumber` inside
`SimulatorPrinterConnector`'s internal `ConcurrentDictionary<string, SimulatedPrinterState>`.

| Field | Type | Notes |
|---|---|---|
| `SerialNumber` | `string` | Dictionary key; identifies which simulated printer this state belongs to. |
| `Status` | `OpenFarm.Domain.Printers.PrinterStatus` (`Idle`, `Printing`, `AwaitingClear`) | The simulator's own notion of what state it's in, reused directly from Domain rather than a parallel enum. |
| `JobStartedAtUtc` | `DateTimeOffset?` | Set to `DateTimeOffset.UtcNow` when `StartSimulatedJob` is called; null while `Idle`. |
| `CycleDuration` | `TimeSpan` | Defaults to ~45s in production; supplied via `SimulatorPrinterConnector`'s optional `cycleDuration` constructor parameter. Tests construct a short duration for fast, deterministic runs. |
| `NozzleTempC` / `BedTempC` | `decimal` | Baseline ambient values while `Idle`; ramp toward target values while `Printing`. |
| `PrintProgressPercent` | `int` | `0` while `Idle`; `0→100` linearly over `CycleDuration` while `Printing`; stays `100` through `AwaitingClear`. |
| `AmsUnits` | `IReadOnlyList<AmsUnit>` | Fixed illustrative default — same static shape every simulated printer reports for the life of the connection. |

### `PrinterStatus` → `gcode_state` string mapping (emit-time only)

| `SimulatedPrinterState.Status` | Emitted `gcode_state` | Resulting `Printer.Status` (via existing `ObserveStatus`) |
|---|---|---|
| `Idle` | `"IDLE"` | `Idle` |
| `Printing` | `"RUNNING"` | `Printing` |
| `AwaitingClear` | `"FINISH"` | `AwaitingClear` |

Transition `AwaitingClear → Idle` ("Mark ready") is triggered the same way it is for a real printer —
`MarkPrinterReadyCommandHandler`'s manual `Printer.TransitionTo(Idle)` — orthogonal to the connector
entirely. `SimulatorPrinterConnector` must not re-assert `"FINISH"` on every periodic tick once
`AwaitingClear` is reached, to avoid flipping a manually-Mark-Ready'd printer back.

## AMS default content (illustrative fixture, unchanged)

One `AmsUnit(UnitIndex: 0, Trays: [...])` with four `AmsTray`s:

| `SlotIndex` | `MaterialType` | `ColorHex` | `RemainPercent` | `Active` |
|---|---|---|---|---|
| 0 | `"PLA"` | `#E53935` | 82 | `true` (while `Printing`) |
| 1 | `"PETG"` | `#1E88E5` | 45 | `false` |
| 2 | `"ABS"` | `#43A047` | 90 | `false` |
| 3 | `"TPU"` | `#FDD835` | 12 | `false` |

`Empty: false` and `SubBrand: "Generic"` for all four. Fixed illustrative constants, not derived from any
request input.
