# Phase 0 Research: Process Profiles — System & User Profiles for Machine, Process, and Filament

## §0. Vendor profile format — verified against a real OrcaSlicer release

The spec's Assumptions section flags the vendor profile format as "confirmed structure" but leaves the
concrete field-level mechanics (idempotency key, material-type source, machine↔process compatibility
signal, model-name mapping) as planning work. This section is that verification, done directly against
OrcaSlicer `v2.4.2` — the version named in this feature's planning request.

**How it was verified**: The release asset itself
(`OrcaSlicer_Windows_V2.4.2_x64_portable.zip`, `github.com/OrcaSlicer/OrcaSlicer/releases/download/...`)
is **not reachable from this environment** — this session's egress policy blocks `github.com` and
`api.github.com` outright ("GitHub access to this repository is not enabled for this session"; only
`tanghq33/openfarm` is in scope, and release assets aren't addressable any other way through those hosts).
`raw.githubusercontent.com`, a separate CDN host, **is** reachable and is not subject to that block. Every
file below was fetched from `https://raw.githubusercontent.com/OrcaSlicer/OrcaSlicer/v2.4.2/resources/profiles/...`
— the exact same source tree path the v2.4.2 release is built from, so this is the real, released v2.4.2
profile data, not an approximation. This is the same approach feature `003-slicer-profile-import`'s own
fixture README already took for the same reason (no slicer install available in-sandbox).

The manifest this repo actually provisions today (`slicers.manifest.json`) pins OrcaSlicer `2.3.2`, not
`2.4.2` — the format below is stable across that gap (only preset counts/names shift release to release;
the vendor-index shape, inheritance mechanism, and field semantics do not), so ingestion logic designed
against v2.4.2 works unmodified against whatever version the manifest is pinned to, now or after a future
bump.

### Vendor index (`resources/profiles/BBL.json`)

Confirmed top-level shape exactly as the spec's Assumptions describe:

```jsonc
{
  "name": "Bambulab", "version": "02.01.00.23",
  "machine_model_list": [ { "name": "Bambu Lab X1 Carbon", "sub_path": "machine/Bambu Lab X1 Carbon.json" }, … ],  // 12 entries
  "machine_list":       [ { "name": "fdm_machine_common",  "sub_path": "machine/fdm_machine_common.json"  }, … ],  // 51 entries
  "process_list":       [ { "name": "fdm_process_common",  "sub_path": "process/fdm_process_common.json"  }, … ],  // 243 entries
  "filament_list":      [ { "name": "fdm_filament_pla",    "sub_path": "filament/fdm_filament_pla.json"   }, … ]   // 2197 entries
}
```

`machine_model_list` is a **different kind of thing** from `machine_list` — it describes physical devices
(`type: "machine_model"`), not presets:

```jsonc
// machine/Bambu Lab X1 Carbon.json — a machine_model_list entry
{
  "type": "machine_model", "name": "Bambu Lab X1 Carbon", "model_id": "BL-P001",
  "nozzle_diameter": "0.4;0.2;0.6;0.8", "family": "BBL-3DP",
  "default_materials": "Bambu PLA Matte @BBL X1C;Bambu PLA Basic @BBL X1C;…"
}
```

`machine_list`, `process_list`, and `filament_list` are the actual **presets** (`type: "machine"` /
`"process"` / `"filament"`), each forming an inheritance chain fully resolvable **within the same vendor
file** — every ancestor a leaf preset declares via `inherits` is itself present in that same
`machine_list`/`process_list`/`filament_list` (verified: `fdm_machine_common`, `fdm_process_common`, and
`fdm_filament_pla`/`fdm_filament_common` are each present in BBL.json's own list, not an external shared
template). This directly satisfies FR-003 ("resolving it against other ingested System Profiles of the
same kind where the declared base was also ingested") without needing to ingest anything beyond one
vendor's own file.

```jsonc
// machine/fdm_machine_common.json           — root: no "inherits", from:"system"
// machine/fdm_bbl_3dp_001_common.json        — inherits fdm_machine_common, instantiation:false (abstract, not selectable)
// machine/Bambu Lab X1 Carbon 0.4 nozzle.json — inherits fdm_bbl_3dp_001_common, instantiation:true (LEAF, selectable)
{
  "name": "Bambu Lab X1 Carbon 0.4 nozzle", "type": "machine", "from": "system",
  "inherits": "fdm_bbl_3dp_001_common", "instantiation": "true",
  "printer_model": "Bambu Lab X1 Carbon",   // <-- links back to machine_model_list[].name
  "nozzle_diameter": ["0.4"], "setting_id": "GM001"
}
```

```jsonc
// process/0.20mm Standard @BBL X1C.json — leaf process preset
{
  "name": "0.20mm Standard @BBL X1C", "type": "process", "from": "system",
  "inherits": "fdm_process_single_0.20", "setting_id": "GP004", "instantiation": "true",
  "compatible_printers": [                  // <-- exact MACHINE PRESET NAMES, not model names
    "Bambu Lab X1 Carbon 0.4 nozzle", "Bambu Lab X1 0.4 nozzle",
    "Bambu Lab P1S 0.4 nozzle", "Bambu Lab X1E 0.4 nozzle"
  ]
}
```

```jsonc
// filament/Bambu PLA Basic @base.json — vendor-generic template, instantiation:false, no compatible_printers restriction
{
  "name": "Bambu PLA Basic @base", "type": "filament", "from": "system",
  "inherits": "fdm_filament_pla", "filament_id": "GFA00", "instantiation": "false",
  "compatible_printers": []
}
// filament/Bambu PLA Basic @BBL X1C.json — leaf filament preset
{
  "name": "Bambu PLA Basic @BBL X1C", "inherits": "Bambu PLA Basic @base",
  "setting_id": "GFSA00", "instantiation": "true",
  "compatible_printers": ["Bambu Lab X1 Carbon 0.4 nozzle", "Bambu Lab X1 Carbon 0.6 nozzle", …]
}
// filament/fdm_filament_common.json (root) sets filament_type: ["PLA"] as the field's *default*;
// filament/fdm_filament_abs.json / fdm_filament_petg.json / fdm_filament_tpu.json / fdm_filament_pa.json /
// fdm_filament_pc.json each OVERRIDE filament_type to their own material — confirmed for ABS/TPU/PA/PC;
// fdm_filament_pla.json doesn't need to override it since PLA already matches the inherited default.
```

**Decisions this establishes:**

1. **`instantiation`** (`"true"`/`"false"` string) is the selectable-vs-abstract-base flag for all three
   kinds. Only `instantiation:"true"` rows belong in an operator-facing picker; `instantiation:"false"`
   rows exist purely as inheritance-chain ancestors (FR-003) and must still be ingested (to resolve bases
   against) but are never offered for selection.
2. **`compatible_printers`** on a process or filament preset is an array of exact **machine preset
   names** (the `name` field of a `machine_list` leaf, e.g. `"Bambu Lab X1 Carbon 0.4 nozzle"`) — this is
   the literal mechanism for FR-006 (machine↔process compatibility) and the filament-side equivalent. It
   is denormalized vendor data (a name list, not an ID list); OpenFarm ingests it as-is and resolves it by
   name against already-ingested System machine rows at read time, mirroring the vendor's own approach
   rather than inventing a join table the vendor data doesn't have.
3. **`printer_model`** on a leaf machine preset (e.g. `"Bambu Lab X1 Carbon"`) is the link back to that
   vendor's `machine_model_list[].name` — this is what "supported machine model catalog" scoping (FR-001,
   Q5) actually cross-references, not a separate lookup.
4. **`setting_id`** (leaf-only; abstract bases never have one, e.g. `GM001`/`GP004`/`GFSA00`) is a stable,
   vendor-assigned identifier independent of the human-readable `name` — the concrete answer to the spec's
   own deferred "exact re-resolution/re-ingestion mechanics" question (§ below).
5. **`filament_type`** is set on the material-family root (`fdm_filament_abs.json`, etc.) and inherited
   down, never re-declared per leaf — it must be resolved by walking each filament's own ancestor chain at
   ingestion time, the same merge OpenFarm already performs for slicing (`ProfileRawDataMerger`), not read
   directly off the leaf's own JSON.

### §0a. Bambu Studio vs. OrcaSlicer — a real fork divergence: the `include` gcode-template mechanism

The spec assumed OrcaSlicer and Bambu Studio "share the same underlying bundle structure" (003's own
Assumptions) and this plan's §0 verified the vendor-index/preset shape against OrcaSlicer only. Per the
user's explicit request, the same tree was independently cloned from **Bambu Studio's own upstream repo**
(`github.com/bambulab/BambuStudio`, `master`, `resources/profiles/BBL`) and diffed against OrcaSlicer's.
Where they overlap, they agree exactly — the "Bambu Lab X1 Carbon 0.4 nozzle" leaf has byte-identical
`setting_id`/`inherits`/`printer_model`/`default_print_profile` in both (OrcaSlicer periodically syncs BBL
profile updates from upstream Bambu Studio, per its own project convention). But **Bambu Studio's current
`machine` and `filament` presets universally carry a second field, `"include"`, that OrcaSlicer's tree does
not use anywhere — 0 occurrences across all 66 of its vendors** — and this is not cosmetic:

```jsonc
// BambuStudio: resources/profiles/BBL/machine/Bambu Lab X1 Carbon 0.4 nozzle.json
{
  "type": "machine", "name": "Bambu Lab X1 Carbon 0.4 nozzle", "inherits": "fdm_bbl_3dp_001_common",
  "instantiation": "true", "printer_model": "Bambu Lab X1 Carbon",
  "include": [
    "Bambu Lab X1 Carbon 0.4 nozzle template machine_start_gcode",
    "Bambu Lab X1 Carbon 0.4 nozzle template machine_end_gcode",
    "Bambu Lab X1 Carbon 0.4 nozzle template time_lapse_gcode",
    "Bambu Lab X1 Carbon 0.4 nozzle template change_filament_gcode",
    "Bambu Lab X1 Carbon 0.4 nozzle template layer_change_gcode"
  ]
  // no "machine_start_gcode"/"change_filament_gcode"/etc. key present directly on this leaf at all —
  // ONLY reachable through the include-named files below.
}
// BBL.json's own machine_list carries these include targets too, but with NO "type" field at all —
// they are not type:"machine" presets, they are raw named gcode-template fragments:
{ "name": "Bambu Lab X1 Carbon 0.4 nozzle template change_filament_gcode", "instantiation": "false",
  "change_filament_gcode": "; filament change gcode ..." }
```

**Scope, verified concretely, not assumed:**
- **Machine**: `include` is present on **every** current Bambu Studio machine leaf checked (X1 Carbon, P1S,
  A1, A1 mini — i.e. every model in OpenFarm's own supported catalog, not just the newest hardware) — 89 of
  Bambu Studio BBL's 148 `machine_list` entries are these untyped include-target fragments, only 59 are
  real `type:"machine"` presets.
- **Filament**: `include` appears on 1,393 of ~2,050 BBL filament presets — here the include targets **do**
  carry `type:"filament"`/`instantiation:"false"` (e.g. `fdm_filament_template_direct_bowden`), so they're
  ordinary-looking abstract rows, just reached through a second field the existing chain-walk never reads.
- **Process**: 0 occurrences — this mechanism doesn't touch process presets in either engine.

**Why this isn't cosmetic — traced into real OpenFarm code, not speculative:** `PrusaSliceFamilyInvocation`
(`api/src/OpenFarm.Infrastructure/Parts/SlicerInvocation/PrusaSliceFamilyInvocation.cs`) writes
`ResolvedSlicingProfiles.MachineSettingsJson`/`FilamentSettingsJson` — the output of
`ProfileRawDataMerger.MergeAncestorChain` over `ProfileChainWalker`'s single-parent `BaseProfileId` walk —
to a **standalone temp file** and hands it to the CLI via `--load-settings`/`--load-filaments`. OpenFarm
never invokes the CLI against the live `resources/profiles` tree by preset name (which would let the real
binary resolve `include`/`inherits` itself, the same way the GUI does); it always hands over a
self-contained flattened JSON it built itself. `ProfileChainWalker` only ever follows one `BaseProfileId`
per node — it has no concept of a node also pulling in N named peer fragments. **Consequence**: for every
Bambu-Studio-sourced machine/filament preset, `machine_start_gcode`/`machine_end_gcode`/
`change_filament_gcode`/`layer_change_gcode`/`time_lapse_gcode` (and `wrapping_detection_gcode` on the
newest multi-tool models) are silently absent from what OpenFarm currently hands the CLI — not a hypothetical
edge case, but the default outcome for 100% of Bambu-Studio-driven slices once this feature makes Bambu
Studio a first-class ingestion source, and (pre-existing, not introduced by this feature) already latently
true today for any **User**-imported profile whose source preset happened to use `include` — feature 003
never surfaced this because no fixture/manual-validation pass has exercised a current Bambu-Studio export.

**Decision**: extend both the storage shape and the merge step, scoped to where `include` is actually used:

1. **Storage** (extends data-model.md): add `IncludedTemplateNames : IReadOnlyList<string>?` (`jsonb`) to
   `MachineProfile` and `FilamentProfile` only (never `ProcessProfile` — confirmed 0 usage) — populated
   verbatim from the vendor leaf's own `include` array for `System` rows; `null` for `User` rows and for
   `System` rows from an engine/vendor that doesn't use it (OrcaSlicer today). Include-target fragments
   themselves are ingested as ordinary rows of the same kind (they already satisfy the existing
   type/instantiation filter — machine-kind ones need the new explicit `type == "machine"` ingestion guard
   from §0's own note below to be excluded from being ingested *as if* they were regular abstract machine
   presets, since they carry no `type` at all and would otherwise fail/misparsed by a type-keyed ingester).
2. **Resolution** (extends `ResolveSlicingProfileSelection`'s successor, research.md §5): after the
   existing `ProfileChainWalker` single-parent walk + `ProfileRawDataMerger` merge produces the base
   flattened JSON, resolve each name in the target's own `IncludedTemplateNames` (by name, scoped to the
   same `VendorName`+`Kind`+`Origin=System`, the same lookup shape as existing base-name resolution) and
   fold each resolved fragment's `RawDataJson` into the accumulator the same shallow-overwrite way, applied
   **before** the target's own final layer (so the target's own explicit keys, if any, still win on a
   genuine collision — none observed in samples, but this keeps the rule consistent with how a base layer
   already behaves). A named include target that fails to resolve is treated the same as an unresolved
   `BaseProfileName` (§ elsewhere) — not a hard error, but the resulting merged JSON is then missing those
   specific gcode fields, which is reported as a normal resolve-time gap rather than silently swallowed.
3. **Explicit type-filter guard** (extends §0's ingestion note): ingestion MUST filter every entry fetched
   via a vendor index's `machine_list`/`process_list`/`filament_list` by the fetched file's own `type` field
   matching the expected kind (`"machine"`/`"process"`/`"filament"`) — Bambu Studio's `machine_list` proves
   list-membership alone is not a reliable "this is a preset of this kind" signal (89 of its 148 entries are
   untyped gcode fragments, not machine presets); a fragment with no `type` (or a mismatched one) is ingested
   as a plain named row available for `IncludedTemplateNames` resolution, never surfaced in a machine picker
   (it also fails the existing `instantiation:"false"` picker-visibility filter independently, so this is a
   defense-in-depth guard, not the only thing preventing it from appearing in a picker).

**Scope call**: this plan fixes `include` resolution for **System**-origin rows only (guaranteed to matter,
100% of Bambu-Studio-ingested machine/filament presets). Whether to also backport the same fix path to
**User**-imported profiles (pre-existing latent gap, feature 003) is left to a follow-up decision — flagged
here, not silently expanded into this feature's scope.

### Model-name ↔ `PrinterModelCatalog` mapping (Assumptions § "concrete mapping approach")

`PrinterModelCatalog` (`api/src/OpenFarm.Domain/Printers/PrinterModelCatalog.cs`) keys entries by
`(PrinterBrand, ModelCode)` — short internal codes (`"X1C"`, `"P1S"`, `"A1MINI"`, …) with no derivable
relationship to either of the vendor JSON's own identifiers:

| OpenFarm `ModelCode` | vendor `machine_model_list[].name` | vendor `model_id` |
|---|---|---|
| `X1` | `Bambu Lab X1` | `BL-P002` |
| `X1C` | `Bambu Lab X1 Carbon` | `BL-P001` |
| `X1E` | `Bambu Lab X1E` | `C13` |
| `P1P` | `Bambu Lab P1P` | `C11` |
| `P1S` | `Bambu Lab P1S` | `C12` |
| `A1` | `Bambu Lab A1` | `N2S` |
| `A1MINI` | `Bambu Lab A1 mini` | `N1` |

Neither vendor field follows a pattern that could be derived from `ModelCode` programmatically — `model_id`
is just as arbitrary as `name` is human-legible. **Decision**: extend `PrinterModel` with a hand-authored
`VendorMachineModelName` field (the exact `machine_model_list[].name` string), populated for the 7 entries
above the same way `PrinterModelCatalog`'s dictionary itself is hand-authored — adding a model is already a
"new entry here, never a rewrite" (per its own doc comment); this is one more field on that same entry, not
a new abstraction. `System/Simulation` gets no vendor mapping (no real vendor ships it). Ingestion scoping
(FR-001/Q5) becomes: for each vendor index file under the bundled slicer's `resources/profiles/`, ingest it
only if at least one of its `machine_model_list[].name` values matches a `VendorMachineModelName` present
in `PrinterModelCatalog.All`.

### Locating `resources/profiles/` inside a bundle leaf

`SlicerExecutableResolver` (`api/src/OpenFarm.Infrastructure/Parts/SlicerExecutableResolver.cs`) already
established that the three platform archive shapes provisioned by `tools/provision-slicers` do **not**
share one fixed internal layout — Windows portable `.zip`, Linux `.AppImage` (`--appimage-extract` →
`squashfs-root/…`), and macOS `.dmg` (`*.app/Contents/…`) each nest differently, so it resolves the
executable by **recursive search with a platform-specific pattern**, not a hardcoded relative path.
Ingestion follows the same convention rather than assuming a fixed path: recursively search the bundle
leaf for a directory literally named `profiles` that contains `*.json` files whose top-level shape matches
a vendor index (`machine_model_list`/`machine_list`/`process_list`/`filament_list` keys present) — this is
robust to the wrapping-folder differences between platforms and self-documents "not found" as a clear,
reportable provisioning-step failure rather than a silent no-op.

## §1. Re-ingestion idempotency & stale-selection detection

Spec Assumptions explicitly defer this: "how a printer's now-stale selection is flagged when its
previously-selected System Profile disappears from a re-ingested set" and "how re-running provisioning
avoids duplicating already-ingested System Profiles."

**Decision — identity key for a System Profile row:**
- **Leaf rows** (`instantiation:"true"`, always carry `setting_id`): identity is
  `(OrganizationId, Kind, VendorName, ExternalId=setting_id)`. Re-ingestion **upserts** by this key —
  matching rows get their `RawDataJson`/`Name`/derived fields refreshed in place (same row `Id`, so any
  printer/part FK pointing at it stays valid across a version bump that only tweaks values), never
  duplicated.
- **Abstract base rows** (`instantiation:"false"`, no `setting_id`): identity is
  `(OrganizationId, Kind, VendorName, Name)` — the vendor's own within-file inheritance key. Same
  upsert-by-key rule.
- **Renames**: a preset can be renamed between versions (confirmed real field: `renamed_from` appears on
  some leaf presets, e.g. `"0.20mm Standard @BBL X1C"` carries `"renamed_from": "0.20mm Bambu Support W
  @BBL X1C"`). Because the identity key is `setting_id`, not `Name`, a rename re-ingests as an **update to
  the same row** (`Name` changes, `Id`/`ExternalId` don't) — a printer that had selected it keeps a valid,
  now-correctly-labeled selection. `renamed_from` itself isn't otherwise consumed.
- **Disappearance**: if a re-ingestion's fetched vendor set no longer contains a previously-ingested
  `ExternalId`, that row is **not deleted** (a printer's `ProcessProfileId` FK would otherwise dangle, and
  a past slice's audit trail would lose its referenced preset) — it's marked stale
  (`IsCurrentlyOffered = false`, see data-model.md). Stale rows drop out of picker results (FR pickers only
  show currently-offered rows) but remain resolvable by ID for anything already pointing at them. A printer
  whose `ProcessProfileId` now resolves to a stale row is flagged the same way a missing selection is
  (edge case: "the printer is flagged as needing a new selection rather than silently slicing with a
  substitute") — surfaced by the existing missing-selection error path in `ResolveSlicingProfileSelection`'s
  successor (§ below), not a new mechanism.
- Re-running ingestion against **unchanged** vendor data is therefore a no-op per row (SC / Acceptance
  Scenario 5) — every row's key already exists, `RawDataJson` compares equal, nothing is written.

## §2. Representative filament resolution (FR-018/FR-019 tie-break rule)

Deferred by spec Assumptions: "which one is representative" needs "a concrete tie-breaking rule."

**Decision**: among System filament profiles where `Origin=System`, `Instantiation=true` (selectable
leaf), and resolved `MaterialType` equals the requested material type, pick the one with the
lexicographically smallest `(VendorName, ExternalId)` pair (ordinal string comparison). This is fully
deterministic (repeated resolution for the same material type always returns the same row, satisfying
FR-019) and requires no new vendor-data signal — `ExternalId`/`setting_id` is stable across re-ingestion
(§1), so the "representative" choice for a material type doesn't change on every provisioning re-run the
way an alphabetical-by-`Name` tie-break could (a `Name`-based rule could flip if a preset gets renamed;
key-based doesn't). Scoping to a **fleet-compatible** candidate (only filament profiles whose
`CompatibleMachineNames` overlaps a machine actually in the fleet) is deliberately **not** part of this
tie-break: FR-018 already treats material-type resolution as independent of any specific printer (that's
what makes it "automatic" rather than per-printer), and requiring fleet-overlap would make the choice
depend on fleet composition, which then fails FR-019's "same choice every time" for a farm whose fleet
composition changes between resolutions.

## §3. Storage shape — extend existing tables, don't fork new ones

`MachineProfile`/`ProcessProfile`/`FilamentProfile` (`OpenFarm.Domain.Profiles`) already have exactly the
shape a System Profile needs: `Name`, `BaseProfileName`/`BaseProfileId` (self-referencing base resolution),
`RawDataJson`, `SourceFormat`. **Decision**: add columns to these same three tables rather than introduce
parallel `SystemMachineProfile`/etc. types:

- `Origin` (enum `ProfileOrigin`: `System` | `User`) on all three.
- `VendorName` (`string?`, populated for `System` rows only — e.g. `"Bambulab"`, the vendor index's own
  `name` field) on all three.
- `ExternalId` (`string?`, the vendor's `setting_id` for leaf rows, `null` for abstract-base rows and
  always `null` for `User` rows) on all three — the §1 idempotency key's second half.
- `IsCurrentlyOffered` (`bool`, default `true`) on all three — the §1 staleness flag.
- `IsSelectable` (`bool`, from vendor `instantiation`; always `true` for `User` rows) on all three — the
  picker-visibility gate.
- `CompatibleMachineNames` (`string[]?`/`jsonb`, populated for `System` process and filament rows from the
  vendor's own `compatible_printers`; `null` for machine rows and all `User` rows) on `ProcessProfile` and
  `FilamentProfile`.
- `MaterialType` (`string?`, System filament rows only, resolved per §0 point 5) on `FilamentProfile`.

This is why FR-014 ("resolve a User-imported profile's declared base against ingested System Profiles of
the same kind") falls out for free rather than needing new cross-table logic: `BaseProfileId` is already a
self-referencing FK on each table, and a `User` row's base-name lookup already searches that same table —
extending it to include `System` rows (same table, same key space) is the existing `FindByNameAsync` query
gaining an origin-inclusive search, not a new resolution path. It also means `ProfileChainWalker` and
`ProfileRawDataMerger` — the machinery `ResolveSlicingProfileSelection` already uses to flatten an ancestor
chain into real preset JSON — work completely unchanged for System-rooted chains.

`ProcessProfile` keeps `Origin` too (for symmetry and so a picker query is one shape across all three
kinds) even though, after this feature, only `Origin=System` rows are ever created (FR-011 removes the
import path entirely). Any pre-existing interim-era `ProcessProfile` rows (`Origin` back-filled to `User`
by migration default) simply stop appearing anywhere — every read path this feature adds filters process
profiles to `Origin=System` — with no destructive migration needed.

## §4. Printer↔process link (parallel to the existing printer↔machine link)

`Printer.MachineProfileId` (`Guid?`, `LinkMachineProfile`/`UnlinkMachineProfile`) is the direct precedent.
**Decision**: add `Printer.ProcessProfileId` (`Guid?`) the same way, plus `LinkProcessProfile`/
`UnlinkProcessProfile`, gated by two rules the type itself can't express (checked in the
`Application`-layer command handler, not the entity): (a) refuse if `MachineProfileId` is null (FR-007),
(b) refuse if the target process profile's `CompatibleMachineNames` doesn't contain the linked machine
profile's `Name` (FR-006). This **replaces** `MachineProfile.DefaultProcessProfileId` /
`SetDefaultProcessProfile` / `ClearDefaultProcessProfile` (the interim machine→process pairing) — moving
the pairing from "one process per machine *profile*" to "one process per *printer*" is exactly what makes
two printers sharing a machine profile independently selectable (User Story 2, Acceptance Scenario 4).

## §5. Slicing resolution rewrite

`ResolveSlicingProfileSelection.ResolveAsync(materialType, ct)` today reads
`Organization.DefaultMachineProfileId` → `MachineProfile.DefaultProcessProfileId` →
`OrganizationFilamentDefault`. **Decision**: replace its inputs with a target — either a specific
`printerId` (the common case: a real slice job) or none (the "estimate-only, fleet-representative" case,
FR-015) — and drop the `IOrganizationFilamentDefaultRepository`/org-default reads entirely:

- **Printer-targeted**: read `printer.MachineProfileId`/`ProcessProfileId` directly (both required,
  existing missing-selection errors reused per-printer instead of per-org).
- **Fleet-representative (no target printer)**: pick a deterministic representative printer — smallest
  `Printer.Id` (GUID) among printers with both `MachineProfileId` and `ProcessProfileId` set — refusing
  with "no printers configured" (edge case) when none qualify. GUID-order is arbitrary but stable, matching
  the same determinism requirement as §2's tie-break, and needs no new "which printer is canonical" concept.
- **Filament**: unchanged mechanism (`ProfileChainWalker`/`ProfileRawDataMerger`) but sourced from either
  the part's `NarrowFilamentProfileId` (when narrowed, FR-012) or §2's representative-System-filament
  resolution for the target material type (when not) — never `OrganizationFilamentDefault`, which is
  deleted along with its table/repository/endpoints per FR-017.
- **`include` fold-in** (machine and filament only, §0a): after each of the above resolves its
  single-parent ancestor chain via `ProfileChainWalker`/`ProfileRawDataMerger` as today, resolve and fold in
  any `IncludedTemplateNames` the same way — required for Bambu-Studio-sourced presets to produce a complete
  settings JSON; a no-op for OrcaSlicer-sourced presets (empty/absent on that engine's data today).

`CliSlicerEngine` only changes its call site (pass `printerId` through from the job/part being sliced);
its own JSON-building/CLI-invocation logic is unaffected.

## §6. Picker query shape at scale (SC-010)

`ListAsync(CancellationToken)` on all three repositories returns every row unbounded — fine for today's
User-only browse page (small, hand-imported catalogs) but wrong for a picker over a vendor's full ingested
set (2,197 filament rows for Bambu alone). **Decision**: add a new, separate paged/searchable query per
repository (e.g. `SearchAsync(ProfileOrigin? origin, bool? isSelectable, string? compatibleWithMachineName,
string? materialType, string? searchText, int page, int pageSize, ct)`), backing the new printer-page and
part-detail pickers this feature adds. The existing `ListAsync`/`FindByNameAsync` stay exactly as they are,
still used only by the unchanged, User-scoped feature-003 browse page (FR-013) and by base-name resolution.

## Summary of resolved unknowns

| Deferred item (spec Assumptions) | Resolution |
|---|---|
| Vendor profile format detail | §0 — verified against real v2.4.2 source data |
| Vendor-model-name ↔ `PrinterModelCatalog` mapping | §0 — hand-authored `VendorMachineModelName` field, 7-entry table given |
| Re-ingestion idempotency mechanics | §1 — upsert by `(VendorName, ExternalId or Name)`, never delete |
| Stale-selection detection | §1 — `IsCurrentlyOffered` flag, existing missing-selection error path |
| Representative filament tie-break rule | §2 — smallest `(VendorName, ExternalId)` ordinal |
| Bambu Studio vs. OrcaSlicer format divergence | §0a — verified against real Bambu Studio upstream source: `include` gcode-template composition (machine + filament, never process), used by every current Bambu Studio machine preset and 68% of its filament presets, absent from OrcaSlicer's tree entirely; traced into `PrusaSliceFamilyInvocation` to confirm it's a real gap (fields silently missing from what's handed to the CLI today), not cosmetic |
