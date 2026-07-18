# Phase 1 Data Model: Printer Fleet Management Refinements

No schema migration accompanies this feature — every change below is either a new domain method over
the existing `Printer` table (`specs/002-printer-integration/data-model.md` remains the authoritative
column-level reference) or a read-only projection over the existing in-code `PrinterModelCatalog`.

## `Printer` (existing table — no column change)

Adds one domain method; no field is added, removed, or retyped.

| Member | Kind | Notes |
|---|---|---|
| `Name` | existing field | Was already privately settable only via `Create`; gains a public mutation path through the new `Rename` method below. |
| `Brand`, `ModelCode`, `SerialNumber` | existing fields | Confirmed immutable for this feature — no method is added that changes them, and FR-010's server-side check (see below) ensures no request can force a change through the edit path either. |
| `IpAddress` | existing field | Continues to be mutated only via the existing `EditConnection(string ipAddress)` method — unchanged by this feature. |
| **`Rename(string name)`** | **new method** | Validates `name` is non-empty (same rule `Create` already applies) and sets `Name`. Does not touch `Status`, does not require any connector interaction, and — unlike `TransitionTo`/`ObserveStatus` — has no state-machine gating, since renaming is orthogonal to a printer's operational status (FR-001: status is never a blocker). |

### Validation rules (additions to `specs/002-printer-integration/data-model.md`'s existing set)

- `Name` must be non-empty on `Rename`, matching the existing rule on `Create`.
- A request that supplies `brand`, `modelCode`, or `serialNumber` differing from the printer's current
  value MUST be rejected in full before any field is mutated (FR-002/FR-010) — enforced in
  `EditPrinterCommandHandler`, not in `Printer` itself, since the printer entity has no reason to know
  about "the caller tried to change an immutable field" as a domain concept; it simply never exposes a
  setter for those fields.
- Changing `IpAddress` (via the optional connection-fields pair, research.md §1) still requires the
  same verify-before-save gate as registration and as today's connection-only edit — unchanged from
  `specs/002-printer-integration/data-model.md`'s existing validation rule.

## `ImmutablePrinterFieldMismatchException` (new domain exception, not a table)

Thrown by `EditPrinterCommandHandler` (Application layer) when a request's optional `brand`/
`modelCode`/`serialNumber` value doesn't match the printer's stored value. Carries a message naming
which field(s) mismatched (e.g. "serialNumber does not match this printer's registered serial
number"). Mapped by `DomainExceptionHandler` to `422 Unprocessable Entity`, alongside the existing
`UnknownPrinterModelException`/`PrinterConnectionVerificationFailedException` entries — same
mechanism, no new middleware or exception-handling path.

## `PrinterModelCatalog` (existing compile-time catalog — no structural change)

Gains a read accessor; the catalog's contents (the `(Brand, ModelCode)` → `PrinterModel` dictionary
from `specs/002-printer-integration/data-model.md`) are unchanged.

| Member | Kind | Notes |
|---|---|---|
| `All` (or equivalent enumerable accessor) | new member | Returns every `(Brand, ModelCode)` entry currently registered, grouped by brand, for `GetPrinterCatalogQuery` to project into a response. Read-only — nothing in this feature adds a way to register a new catalog entry at runtime. |

### `PrinterCatalogResponse` (new API response shape, not persisted)

```jsonc
{
  "brands": [
    { "brand": "Bambu", "models": ["X1", "X1C", "X1E", "P1P", "P1S", "A1", "A1MINI"] },
    { "brand": "System", "models": ["Simulation"] }
  ]
}
```

Directly mirrors `PrinterModelCatalog`'s current contents (research.md §4) — FR-008 requires this to
always match what `RegisterPrinterCommandValidator`/`Printer.Create` would accept, which holds by
construction since both read the same static catalog.

## `ConnectionSecret` (existing table — no schema change)

Unaffected in shape. The broadened edit's connection-changed path (research.md §1/§2) writes a new
`ConnectionSecret` row keyed by `(OrganizationId, SerialNumber)` exactly as today's connection-only
edit does — `specs/002-printer-integration/data-model.md`'s description of this table remains
accurate without amendment.

## `Activity History Entry` (existing table — no schema change)

The broadened edit continues to produce exactly one entry per successful mutation via the existing
`IActivityLoggable`/`ActivityLogPostProcessor` pipeline (research.md §8) — `EntityType = "Printer"`,
`EventType = "PrinterEdited"` (renamed from `"PrinterConnectionEdited"` to reflect the broadened
scope). The relocated detail-page Remove action produces the same `EventType = "PrinterRemoved"`
entry the fleet-list Remove action already does today — no change to `RemovePrinterRequest` at all,
only to which UI surface calls it.
