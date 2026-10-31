# Exact quantities and object skipping

Finite **Units** and Assembly **Sets** orders now create full plates followed by
one partial plate. Five sets requiring two brackets each, with six brackets per
plate, create jobs targeting **6 + 4** brackets. Assembly requirements are
aggregated before splitting. Component plate overrides are rejected; **Plates**
orders continue to request full plates. Fractional printed-unit targets are
rejected.

`PlateQuantity` remains the original capacity for compatibility. `TargetUnits`
records the new job's commitment. Produced and missing units come from the
retained complete-unit selection and confirmed object outcomes, rather than the
capacity of the source plate. Existing jobs without these fields retain their
historical whole-plate accounting.

## Automatic object quantities

Every printable object instance is one unit, regardless of name, shape, or left/right designation.
Shared meshes and component wrappers do not combine units. Modifiers, supports and shared purge
structures are not separate output units. Quantity detection and skip eligibility are separate:
a known quantity does not prove that native skip identities are available.

A shared inventory reads selected-plate model instances for plain 3MF, labelled G-code and
slice metadata for sliced 3MF, and native labels for raw G-code. An empty
`model_settings.config` does not hide populated `slice_info.config`. Repeated human-readable
names and thumbnail bounding-box IDs never establish native skip identities. Unknown counts
remain distinguishable from confirmed singletons.

Recognized quantities are read-only. Import, file replacement and explicit reprocessing refresh
the count; there is no background repair of existing Parts. Saved counts that disagree with the
file block new submissions with reprocessing guidance. Unrecognized files retain manual
whole-plate estimates without enabling object skipping.

Automatic mappings contain exactly one object per unit. The grouping editor has been removed;
the compatibility `object-groups` endpoint only accepts the canonical singleton mapping.
Existing job JSON remains readable for history. Incompatible unsent jobs and retries are blocked
rather than silently reinterpreted: reprocess the Part and create new work. Finite source
snapshots and completed accounting are never rewritten by these fixes.

Mappings belong to the exact source revision and plate. Generated output must preserve mapped
identities; changed, missing, unsupported, or mixed identities block dispatch. Plain Bambu/Orca
projects with positive `identify_id` metadata require slicing and final-artifact verification.
Klipper polygons supplement the labelled object list when present.

Bare Bambu G-code can have a reliable quantity from M624-associated unique labels, while still
lacking the sliced object identity table needed to verify native skipping. The current upload
wrapper adds G-code and checksum entries only; wrapping does not prove skip eligibility.
Use a labelled sliced 3MF for partial-plate or live object skipping in this case. Normal
whole-plate printing remains available. Orca production slicing enables object labels and
exclusion commands. No motion removal, automatic packing, duplication, or name-based matching
is performed.

## Printer transports and enablement

- **Bambu:** the start request includes native `skip_objects`; live requests use
  `skip_objects` / `obj_list`. Confirmation comes from `print.s_obj`.
- **Snapmaker U1:** the execution G-code inserts `EXCLUDE_OBJECT NAME=...` after
  startup/reset and immediately before the first object. Later resets are
  rejected. Live requests use Moonraker `printer.gcode.script`; confirmation
  comes from `exclude_object.excluded_objects`.

Reference implementations: [Bambu Studio live commands/telemetry](https://github.com/bambulab/BambuStudio/blob/master/src/slic3r/GUI/DeviceManager.cpp),
[BambuLabs API start exclusions](https://github.com/bambutools/bambulabs_api/blob/main/bambulabs_api/mqtt_client.py),
[Snapmaker exclusion module](https://github.com/Snapmaker/u1-klipper/blob/main/klippy/extras/exclude_object.py),
and [Klipper's reset and file contract](https://www.klipper3d.org/Exclude_Object.html).

Both operations are disabled until hardware verification is recorded in deployment
configuration. `ObjectSkipping:VerifiedStartPrinters` and
`ObjectSkipping:VerifiedLivePrinters` are separate arrays of exact printer serial
numbers, empty by default. For example, after validation:

```json
"ObjectSkipping": {
  "VerifiedStartPrinters": ["VALIDATED-SERIAL"],
  "VerifiedLivePrinters": ["VALIDATED-SERIAL"]
}
```

Before adding a serial, verify the installed firmware, file dialect, ordinary and
multipart selections, multi-filament/tool mappings, exclusion telemetry,
reconnection, and remaining output. Confirm that surplus objects receive **no
object extrusion from the beginning**. Purge and shared structures may still
print. Record firmware versions with deployment validation notes and remove the
serial from these arrays after firmware/configuration changes until revalidated;
OpenFarm does not currently track firmware identity automatically.

Matching excludes unverified partial-plate destinations. Live skipping also
requires the printer's reported file identity to match the job artifact. The UI
shows availability before allowing selection. No physical printer verification
was performed as part of implementation.

## Completion, recovery, and files

Commands persist intent before transmission. Telemetry confirms exclusions;
unknown identities or missing confirmation keep completed output unverified.
A print found completed after an interrupted connection also requires physical
output confirmation. Use **Confirm produced units** only after checking the physical plate. Until
then, replacements/write-offs of a completed attempt are blocked.

After a confirmed partial success, **Requeue missing units** queues only the
unresolved shortfall, using the original snapshot and queue placement. **Write
off missing units** resolves that loss separately from produced stock. Failed or
cancelled attempts conservatively produce zero units. Replacement and write-off
operations retain concurrency protection and cannot resolve the same loss twice.

An uncertain start acknowledgement is never automatically resent for an
object-aware job, including after restart. Telemetry may still confirm its start.
Otherwise check the printer is idle and the bed is clear, then use **Confirm print
did not start** to cancel the attempt before requeueing.

The execution file, checksum, selected units, planned exclusions, and confirmed
exclusions are retained independently of catalog edits/removal. Downloads of sent
jobs use the retained file, without re-slicing against current printer settings.
**Bambu exclusions are start-request options:** printing a downloaded Bambu file
outside OpenFarm requires applying the same exclusions. U1 downloads contain
their initial exclusion commands.

Filament remains an estimate. Accounting uses object-region extrusion, retaining
startup purge, shared moves and extrusion before observed live skips; it does
not scale by the final good-unit count. If skip timing is unavailable, accounting
conservatively retains that object's extrusion. Unavailable retained files do not
block terminal state processing; the last estimate is retained and an activity
entry identifies the unavailable recalculation.

Continuous backfill retains full-plate scheduling. Its coverage and cumulative
limits use produced units for completed jobs and reserved target units for active
attempts.

## API additions and rollout

- `GET /parts/{id}/objects`, `PUT /parts/{id}/object-groups`.
- `GET /print-jobs/{id}/objects`, `POST /print-jobs/{id}/skip-objects`.
- `POST /print-jobs/{id}/object-output` and
  `POST /print-jobs/{id}/confirm-not-started` for explicit recovery.
- Existing job, queue, plan, and Run responses expose target/output/shortfall
  information. Mutations retain permission, tenancy, audit, stale-artifact and
  complete-unit validation.

Apply the additive `ExactQuantityObjectSkipping` migration through normal API
startup. No historical selections/skips are fabricated. Downgrade is refused
when it would discard mappings or meaningful exact-quantity executions.

Automated tests cover quantity splits, mapping validation, transports, output
confirmation, exact replacement, snapshots, accounting and keyboard interactions.
Physical hardware validation and user visual checks (responsive layout, both
themes, 200% zoom, focus and stacking) remain required before rollout.
