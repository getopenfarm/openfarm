# Phase 0 Research: Virtual Printer (Simulated Printer for UI Testing)

**Supersedes**: an earlier version of this document (and the implementation built from it) that routed
simulated printers off a reserved IP-address range (`192.0.2.0/24`, RFC 5737 TEST-NET-1). The
2026-07-17 clarification follow-up reversed that decision: a simulated printer is now selected through
a real, user-visible Brand/Model catalog entry, not an invisible connection-detail convention. This
document reflects the new decision only; see spec.md's Clarifications for the reversal's rationale.

## 1. How the connector layer recognizes a printer to simulate

**Decision**: A printer is routed to the simulator when its own `Brand` is `"System"` and its
`ModelCode` is `"Simulation"` — an ordinary `PrinterBrand`/`PrinterModelCatalog` entry (Domain layer),
selected by the user through the same Brand/Model catalog selectors every other printer uses, not
inferred from `IpAddress` or any other connection detail.

**Rationale**: The prior IP-range convention was invisible by design — nothing in the Add-printer dialog
hinted that a specific address range meant "simulated," so a tester who didn't already know the magic
range from documentation had no way to discover the simulator (the exact discoverability gap the
2026-07-17 clarification follow-up raised). A catalog entry fixes this the same way every other printer
brand/model is discovered: it's a selectable option in the same dropdown. `PrinterModelCatalog`'s own doc
comment already frames "adding a brand/model is a new entry here, never a rewrite" as the intended
extension point — the simulator is not a special case of that pattern, it's an ordinary use of it.

**Alternatives considered**:
- *Keep the IP-range convention, add a frontend-only "Use simulated printer" helper button that
  auto-fills a reserved address*: rejected per the clarification — the user explicitly wants the
  simulator to be a first-class catalog citizen, not a value auto-filled behind a generic IP field.
- *Keep IP-range detection as a fallback alongside catalog selection*: rejected — two coexisting
  detection mechanisms is exactly the "dual-path" confusion the constitution's Principle IV
  (no preemptive/parallel abstractions) warns against once a single, clear mechanism suffices.
- *A sentinel `SerialNumber` prefix instead of a catalog entry*: still has the original discoverability
  problem (invisible unless documented) and was already rejected in the original research pass for the
  same reason; catalog-based selection is strictly more discoverable.

## 2. Passing Brand/Model through to the connector-selection point

**Decision**: Extend `PrinterConnectionDetails` (Application-owned, `OpenFarm.Application.Printers`) with
two more fields:

```csharp
public sealed record PrinterConnectionDetails(string Brand, string ModelCode, string IpAddress, string SerialNumber, string AccessCode);
```

`IPrinterConnectorFactory.CreateConnector(PrinterConnectionDetails details)` /
`CreateCameraRelay(PrinterConnectionDetails details)` keep their exact existing method signatures — no
interface change — since every caller already builds a `PrinterConnectionDetails` locally and already has
`Brand`/`ModelCode` in scope at that point:

- `VerifyPrinterConnectionEndpoint` — `VerifyPrinterConnectionRequest` gains `Brand`/`ModelCode` fields
  (it had none before; the dialog's pre-flight verify call must now tell the backend which
  brand/model the user selected so verify routes to the same connector Register will use).
- `RegisterPrinterCommandHandler` — `RegisterPrinterCommand` already carries `Brand`/`ModelCode`; no
  command shape change, just include them when constructing `PrinterConnectionDetails`.
- `EditPrinterConnectionCommandHandler` — already loads the `Printer` entity (`printer.Brand`,
  `printer.ModelCode`) before building `PrinterConnectionDetails`; no new dependency.
- `PrinterConnectionBackgroundService.RunConnectionLoopAsync` — already loads `printer` before building
  `details`; same pattern.

`PrinterConnectorFactory` (Infrastructure) drops `SimulatedPrinterAddressRange`/`IPAddress` parsing
entirely and routes purely on `details.Brand`/`details.ModelCode`:

```csharp
private static bool IsSimulated(PrinterConnectionDetails details) =>
    string.Equals(details.Brand, "System", StringComparison.OrdinalIgnoreCase)
    && string.Equals(details.ModelCode, "Simulation", StringComparison.OrdinalIgnoreCase);
```

**Rationale**: This keeps `IPrinterConnectorFactory`'s own method signatures — and every existing
`IPrinterConnector`/`IPrinterCameraRelay` implementation's methods — completely unchanged (FR-011/
SC-006's "no changes to the connector abstraction itself"). Only the passive data record every call site
already constructs gains two fields those call sites already have on hand; no new orchestration, no new
service dependency, no interface churn.

**Alternatives considered**:
- *Add `brand`/`modelCode` as new parameters directly on `IPrinterConnectorFactory.CreateConnector`/
  `CreateCameraRelay`*: rejected — this does change the connector-abstraction's own method signatures
  (three params instead of one), a larger footprint than extending the data record every caller already
  builds. Functionally equivalent, but more churn for no benefit.
- *Look the printer's Brand/ModelCode up again inside `PrinterConnectorFactory` via a repository call*:
  rejected — turns a pure, synchronous, side-effect-free factory into one with I/O and a new
  dependency, for data every caller already has in hand two lines earlier.

## 3. Domain catalog entry

**Decision**: Add one `PrinterBrand` enum member and one `PrinterModelCatalog` entry:

```csharp
public enum PrinterBrand
{
    Bambu,
    System,
}
```

```csharp
[(PrinterBrand.System, "Simulation")] = new PrinterModel(PrinterBrand.System, "Simulation", CameraTransport.BinaryImage),
```

**Rationale**: `CameraTransport.BinaryImage` is chosen (not a new `CameraTransport` member) so the
existing camera-loop gate in `PrinterConnectionBackgroundService.RunConnectionLoopAsync`
(`PrinterModelCatalog.TryResolve(...).CameraTransport == CameraTransport.BinaryImage`) triggers
unmodified — no orchestration change, and every simulated printer now gets full camera fidelity
unconditionally (previously this depended on which Bambu model code the tester happened to pick;
now there is exactly one simulated model, and it always qualifies). This directly strengthens FR-006
("MUST produce a camera image on its own") from "true if you picked the right model" to "always true."

`Printer.Create`'s existing `PrinterModelCatalog.Resolve(brand, modelCode)` call needs no change — the
new catalog entry makes `(System, "Simulation")` resolve exactly like any other (brand, model) pair.

**Alternatives considered**:
- *A dedicated `CameraTransport.Simulated` member*: rejected — would require a matching branch in the
  background service's camera gate, the exact kind of simulation-specific special-casing FR-002 rules
  out; `BinaryImage` already produces the right orchestration behavior with zero new branches.

## 4. Frontend: Brand selector added, Model list scoped per brand

**Decision**: `AddPrinterDialog.tsx` gains a `brand` field (`'Bambu' | 'System'`) rendered as a `<select>`,
replacing today's hardcoded `brand: 'Bambu'` in `onSubmit`. The existing `modelCode` `<select>`'s option
list becomes a function of the selected `brand`:

- `Bambu` → `X1, X1C, X1E, P1P, P1S, A1, A1MINI` (unchanged from today).
- `System` → `Simulation` (the catalog's only System-brand entry).

Changing `brand` resets `modelCode` to that brand's first/only model. `verifyMutation`'s payload and
`registerMutation`'s payload both include the selected `brand` (no more hardcoded value). IP address,
serial number, and access-code fields stay visible and required regardless of brand — the user supplies
any placeholder values for a Simulation printer, exactly as for a real one (2026-07-17 clarification).

**Rationale**: This is the smallest change that makes Brand a real, user-visible selection instead of a
hardcoded constant — today's dialog already has zero Brand UI at all, so introducing the selector is
unavoidable once Brand must be chosen rather than assumed. Scoping the Model list per Brand (rather than
one flat list of every brand's models) avoids a user picking a nonsensical (Bambu, Simulation) or
(System, P1S) pair the catalog would reject with a 422.

**Consequence — Orval regeneration required**: `VerifyPrinterConnectionRequest`'s OpenAPI shape changes
(gains `brand`/`modelCode`), so the generated API client (`web/src/lib/api/generated`) must be
regenerated (`bun run` Orval step) before the dialog can call the updated hook shape. This is a normal,
expected step per `CLAUDE.md`'s "never hand-edit generated files; regenerate from the spec instead" rule
— not a new process.

**Alternatives considered**:
- *Keep Model as one flat dropdown across all brands, infer Brand from the chosen Model*: rejected —
  works today only because there's one brand; it stops being unambiguous the moment a second brand
  exists (this feature is exactly that moment), and it's backwards from the natural selection order
  (brand, then model).

## 5. Retiring the IP-range convention

**Decision**: Delete `SimulatedPrinterAddressRange.cs` outright. No code path checks `IpAddress` for
simulation purposes anywhere after this change.

**Consequence for tests**: Every existing test (`PrinterConnectorFactoryTests`,
`RegisterPrinterEndpointTests`, `MarkPrinterReadyEndpointTests`, `RemovePrinterEndpointTests`,
`SimulatedPrintLifecycleTests`) that previously used a `192.0.2.0/24` address purely to route through the
simulator and avoid a real MQTT connection attempt in CI must instead register with
`Brand: "System", ModelCode: "Simulation"` — the IP address they use no longer matters functionally and
can be any well-formed address (a placeholder is fine, matching how a real user would fill the field).
Conversely, the one regression test proving a real brand still resolves to `BambuLanConnector` must use
a Bambu brand/model (any IP), not "any non-reserved IP" as before.

**Rationale**: FR-003 (2026-07-17 revision) requires the connector layer to route purely on Brand/Model,
so leaving the IP-based branch in place — even disabled — would be dead code contradicting the very
requirement this phase implements. This is a straightforward mechanical test migration, not a design
question: every test's *intent* (get the simulator without touching real hardware, or confirm a real
brand still reaches `BambuLanConnector`) is unchanged, only the mechanism it flips to get there differs.

## 6. Everything else (unchanged from the original research pass)

The following decisions from the original (superseded) research.md are unaffected by this reversal and
carry forward unchanged — the simulator's own *behavior* was never in question, only how it's *selected*:

- `SimulatorPrinterConnector`'s `VerifyAsync` sentinel (`AccessCode == "wrong"` → unverifiable).
- The Idle → Printing → AwaitingClear → Idle lifecycle, `StartSimulatedJob`'s test-only trigger, and the
  status-flapping-avoidance rule (never re-assert `"FINISH"` on a stable tick).
- The fixed illustrative 4-tray AMS default fixture.
- `SimulatorPrinterCameraRelay`'s placeholder-JPEG generation.
- The optional-`TimeSpan cycleDuration` constructor-parameter pattern for deterministic tests.
- No database schema change — simulated state remains entirely in-memory and non-durable.
