# Printer hardware model

How OpenFarm describes printer hardware, and where each kind of knowledge lives. The goal: a new printer model is a
catalog entry plus, at most, adapter work — never a change to business logic or the web.

## Two homes

| Question | Home | Example |
| --- | --- | --- |
| What is this model? | `PrinterModelCatalog` (`api/src/OpenFarm.Domain/Printers`) | driver, extruder labels, feed unit style, camera transport, plates, slicer model name |
| How do we talk to it? | The driver's adapter (`api/src/OpenFarm.Infrastructure/Printers/<Driver>`) | MQTT topics, Bambu tray ids (`254`, `unit * 4 + slot`), `ams_mapping`, Moonraker objects and head numbers, `map_table`, URL schemes |

This is the split Printago draws between *capabilities* and *bindings*, kept as typed C# rather than JSON documents.

Rules:

- No brand or model checks outside the catalog and the adapters. Code that needs a hardware fact asks the model
  (`PrinterModelCatalog.Find(brand, modelCode)`), e.g. `model.UsesAccessCode`, `model.CameraSupported`,
  `model.Driver == PrinterDriver.Simulator`.
- The connector and camera relay are chosen by `PrinterModel.Driver` (`PrinterConnectorFactory`). A model the catalog
  doesn't know is refused (`UnknownPrinterModelException`), never guessed to be a Bambu.
- The web never branches on brand, model or driver. Everything it shows per model comes from `PrinterModelResponse`,
  served with the catalog, the fleet list and the printer detail (connection fields and their copy, camera support),
  and from the readings and feed units, which carry the server's unit and slot labels.

## The catalog entry

`PrinterModel` (`PrinterModel.cs`):

| Field | Meaning |
| --- | --- |
| `Driver` | The adapter: `BambuLan`, `SnapmakerMoonraker`, `Simulator`. |
| `Extruders` | One label per extruder, in index order: `["Nozzle"]` on a single-nozzle printer, `["T1"…"T4"]` on the U1. |
| `FeedUnitStyle` | How the connector-derived multi-material unit is named: `Ams` ("AMS n"), `Toolheads` (one unit whose slot n is the spool on extruder n, labelled with that extruder's name), `MultiMaterialUnit`. |
| `CameraTransport` | `BinaryImage` and `HttpSnapshot` have relays; `Rtsp` (X1 family) has none yet. |
| `VendorMachineModelName` | The slicer's `printer_model` name; scopes profile ingestion and checks pre-sliced files. |
| `CompatiblePlateTypes` | The model's own plates. |

Derived: `UsesAccessCode` (registration needs a typed serial and access code; false for the Moonraker driver, which
reads the serial from the printer and takes an optional API key) and `CameraSupported`.

## Feeds and extruders

Domain and Application name a feed position only by `FeedSlotRef(Kind, UnitIndex, SlotPosition)`. Adapters translate
their own numbering at the edge: Bambu tray ids live only in `Infrastructure/Printers/Bambu`, Moonraker head numbers
only in `Infrastructure/Printers/Snapmaker`.

| Type | Meaning |
| --- | --- |
| `FeedUnitReading` / `FeedSlotReading` | What the printer reports now, per unit kind: multi-material units and the external spool (unit index = extruder). Each slot names the extruder it feeds. Cached on `Printer.FeedReadings`; a message replaces only the kinds it reports. |
| `ExtruderReading` | Per extruder: the reported nozzle diameter and the slot it is feeding (`ActiveSlot`). Cached on `Printer.Extruders`. |
| `FeedSlot.ExtruderIndex` | The persisted slot's extruder, reconciled from the readings by `FeedSlotTelemetrySyncHandler`. |
| `FilamentFeedMapping.Tools` | Dispatch: one `FeedSlotRef?` per project filament; each adapter encodes it (`ams_mapping`, `map_table`). |

The shared-inlet rule is per extruder (`ProductionFeedPath.ActiveSlotIds`): an extruder with a multi-material unit
uses either that unit or its external spool, whichever its `ActiveSlot` says; an extruder without one uses its
external spool.

## Nozzles

Each extruder reports its own nozzle (`Printer.ObserveNozzles`) and is confirmed on its own:

- A report confirms each extruder at its own diameter, so a mixed report is confirmed, not refused. An unknown
  diameter unconfirms only that extruder and keeps its last diameter for display.
- A new connection, a connection edit or lost reachability unconfirms every extruder (`BeginConnection`).
- `Printer.ConfirmedNozzleMm(extruder)` and `ConfirmedNozzlesMm()` are what production reads. `NozzleConfirmed` means
  "some extruder is confirmed"; `ReportedNozzleDiameterMm` is the nozzle every extruder shares, or null when they
  differ, for display only.

A slot can serve a job only when its own extruder's confirmed nozzle equals the part's nozzle
(`ProductionDispatchMatcher.CanServe`); extruders the job doesn't use may differ. The printer still has one machine
preset, and every `nozzle_diameter` in it must equal the job's nozzle. Backup stock (a reload continuing onto another
slot, `FilamentCover.Available`, `SpoolUsageRecordedHandler`) never crosses to another nozzle size. Profile
assignments, machine-profile pickers and the readiness report work per distinct nozzle. Diagnostics name the
extruder with the wrong nozzle (`extruder-nozzle` check, models with more than one extruder).

The printer detail page's Hardware card shows the model, driver, camera and one row per catalog extruder.

## Checklist: add a printer model on an existing driver

1. Add the entry to `PrinterModelCatalog.Entries` (and the discovery code mapping, e.g. Bambu SSDP device codes).
2. Add any protocol quirk to the driver's adapter, keyed by model code inside the adapter (e.g. the Bambu URL scheme
   list in `BambuLanConnector`).
3. Extend `PrinterModelCatalogTests`. No Application, Domain-rule or web change should be needed; if one is, the
   hardware fact belongs in the catalog.

## Checklist: add a driver

1. Add a `PrinterDriver` value and an adapter implementing `IPrinterConnector` (and `IPrinterCameraRelay`,
   `IPrinterDiscovery` when the printer offers them). Translate the printer's protocol into the existing contract
   inside the adapter; no Application or Domain code may know the protocol.
2. Route the driver in `PrinterConnectorFactory`.
3. Map its connection fields and copy in `PrinterConnectionFieldsResponse.For`.
4. Add its catalog entries, then fixture-replay tests against a fake transport (see `docs/SNAPMAKER_U1.md`).

## Next: Bambu H2D (dual nozzle)

Not built; there is no hardware to test on. The vendored fixtures
`third_party/printers/bambulab/references/ha-bambulab/custom_components/bambu_lab/pybambu/tests/MOCK-H2D*.json` are
the target. What it still needs, all inside the catalog, the Bambu adapter and slicing:

1. **Catalog:** `(Bambu, "H2D")` with extruders `["Right", "Left"]` (ha-bambulab: 0 = right/main, 1 = left), RTSP
   camera, its plates and SSDP code.
2. **Parser:** `device.extruder.info[]` (`snow = (ams_id << 8) | slot`, `temp` packs target and current),
   `device.extruder.state` bits 4–7 = active extruder, `device.nozzle.info[]` (ids under 16; diameter and type such as
   `HS01`), `vir_slot` 255/254 as one external unit per extruder, and each AMS's extruder from `(info >> 8) & 0xF`
   (cross-checked against `snow` in `MOCK-H2D.json`; the vendored doc names the sides the other way round, so confirm
   on hardware).
3. **Dispatch:** a project-length `ams_mapping` plus `ams_mapping2`, the external spool as 255/254 per extruder.
4. **Slicing:** write `filament_map` from each slot's extruder through the preset's `physical_extruder_map`, and
   `nozzle_volume_type` per extruder.
5. **Small additions:** `FeedPlacementResolver` returns the slot's own external unit; the AMS/external mixing rule
   becomes per extruder; a reloader inherits the extruder of the port it replaces (`FeedUnitDisplay` and the web's
   `feedSlotView` still use the one external port); `FeedUnitDisplay` adds " · {extruder}" to a unit feeding one of
   several extruders ("AMS 1 · Left"), so the web needs no change.
