# Contract Delta: filament match source on part responses

This is an additive delta on top of the existing Part Library contract
(`specs/007-part-library/contracts/part-library-api.md`) — no endpoint is added, removed, or renamed, and
every existing field on every response below is unchanged. Two new nullable fields appear on any response
shape that already carries `estimatedPrintTimeMinutes`/`estimatedFilamentWeightGrams`.

## Affected responses

- `GET /api/v1/parts/{id}` (part detail)
- Any job-preview response shape that already includes the preview estimate fields (per
  `specs/007-part-library/contracts/part-library-api.md`'s `preview.estimateStatus` note)

## New fields

```jsonc
{
  // ...every existing field, unchanged...
  "estimatedPrintTimeMinutes": 47,
  "estimatedFilamentWeightGrams": 12.5,

  // new:
  "estimatedFilamentMatchSource": "FleetLoaded", // "FleetLoaded" | "FilamentLibrary" | "SystemCatalog" | null
  "estimatedFilamentMatchLabel": "Fleet: Printer 3" // human-readable, null iff the source is null
}
```

- Both fields are `null` together whenever no estimate has been computed yet (mirrors
  `estimatedPrintTimeMinutes`/`estimatedFilamentWeightGrams`'s own nullability — same lifecycle, no new
  "pending" state introduced) or when the part's material never went through filament matching at all (an
  already-sliced file whose material came from its own embedded metadata, never a matched stand-in).
- `estimatedFilamentMatchSource` is a fixed three-value enum, serialized as a string like every other
  string-backed enum already on this contract (e.g. `estimateStatus`).
- `estimatedFilamentMatchLabel` follows the existing "System: {vendor}" / "User" origin-labeling convention
  already shipped in the Machine/Process/Filament profile picker
  (`web/src/features/profiles/components/ProfilePickerCombobox.tsx`), extended to the three sources above:
  - `FleetLoaded` → `"Fleet: {printer name}"`
  - `FilamentLibrary` → `"Library: {filament label}"` (the spool's own label if set, otherwise
    `"{Material} - {ColorName}"`)
  - `SystemCatalog` → `"System: {vendor name}"`

## No request-shape changes

Nothing here adds a request field, an override, or a way for an operator to force a match source — this
delta is read-only visibility into what the matching capability already decided (spec `FR-013`/`SC-003`).
Narrowing a part's filament requirement to a specific profile or library filament (existing
`filamentRequirementOverrides` from the Confirm contract) is unaffected and continues to bypass the matching
capability entirely, per `FR-011`.
