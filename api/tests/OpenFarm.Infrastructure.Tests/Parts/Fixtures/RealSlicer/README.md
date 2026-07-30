# Real-slicer golden-file cases

Each subfolder here is one golden-file test case for `RealSlicerGoldenFileTests` — a real STL/3MF
model plus the actual G-code a real slicer CLI produces for it, used to catch regressions in the
toolpath output itself (not just the estimate/error orchestration around it, which every other
slicing test already covers against a deliberately-broken stub binary).

**Comparison strategy differs by input format:**
- `.3mf` cases get a full normalized G-code diff (`GCodeNormalizer`) — a project file preserves the
  object's exact position/rotation, so an identical toolpath is both meaningful and achievable.
- `.stl` cases only compare estimated print time and filament weight within a 5% tolerance (the same
  values `GCodeMetadataHelper` parses in production). A bare `.stl` carries no object placement, so
  OrcaSlicer has to auto-arrange it onto the bed — that placement can legitimately differ by a few mm
  between a GUI session and a headless CLI invocation with identical settings, which would make a full
  toolpath diff fail on placement noise rather than real regressions.

These tests only run when a real slicer bundle is installed locally (see `RealSlicerBundleLocator`
— `.slicers/` at the repo root, or `OPENFARM_TEST_SLICER_BUNDLE_ROOT`). They are not run in CI.

## Adding a new case

1. Pick the exact pinned `(engine, version)` for your case from `slicers.manifest.json` at the repo
   root — don't use a newer install; some newer OrcaSlicer/BambuStudio builds are documented there as
   crash-prone.
2. Slice your model for real with that slicer, using a machine/process/filament preset that ships
   with the real install itself (e.g. `.slicers/orcaslicer/2.3.1/macos/OrcaSlicer.app/Contents/Resources/profiles/`
   — browse the relevant vendor's `.json` index there for valid preset names). The harness ingests
   these same real, complete presets at test time via the real `SystemProfileIngestionService`
   (`VendorProfileFixtureStaging`) — it does **not** use the small hand-curated fixtures under
   `../../Profiles/Fixtures/VendorIndex/`, which exist only to unit-test the ingestion parser in
   isolation and are deliberately incomplete (not slice-ready).
3. Save the model as `model.stl` or `model.3mf`, and the raw sliced G-code as `expected.gcode`:
   - OrcaSlicer/BambuStudio: unzip `Metadata/plate_1.gcode` out of the exported `.gcode.3mf`.
   - SuperSlicer: its plain `.gcode` output, as-is.
4. Create a new folder `<engine-slug>-<version>-<short-name>/` here containing `model.stl`/`model.3mf`,
   `expected.gcode`, and a `case.json`:
   ```json
   {
     "modelFileName": "model.stl",
     "modelFileFormat": "stl",
     "expectedGCodeFileName": "expected.gcode",
     "materialType": "PLA",
     "nozzleDiameterMm": 0.4,
     "machineProfileName": "Bambu Lab X1 Carbon 0.4 nozzle",
     "processProfileName": "0.20mm Standard @BBL X1C",
     "filamentProfileName": "Bambu PLA Basic @BBL X1C",
     "bedType": "Textured PEI Plate"
   }
   ```
   `machineProfileName`/`processProfileName`/`filamentProfileName` are only needed for OrcaSlicer/
   BambuStudio (SuperSlicer uses plain scalar flags and ignores them — omit or leave null).
   `modelFileFormat` is matched case-insensitively (`"stl"`/`"Stl"`/`"STL"` all work) — use lowercase
   for consistency with the rest of the file. Valid values: `stl`, `threemf`, `gcode`.
   `bedType` is optional — set it to whatever plate you selected in the GUI when producing the golden
   fixture (e.g. `"Textured PEI Plate"`, `"Cool Plate"`); it isn't declared in any machine/process
   preset file, so without this override the harness would slice against a different default plate
   than the one you actually used.
5. Add one test method to `RealSlicerGoldenFileTests`:
   ```csharp
   [RealSlicerFact(SlicerEngine.OrcaSlicer, "2.3.1")]
   public Task MyNewCase() => RunCaseAsync("<your-folder-name>", SlicerEngine.OrcaSlicer, "2.3.1");
   ```
6. Run it: `dotnet test api/OpenFarm.slnx --filter "FullyQualifiedName~RealSlicerGoldenFileTests"`.
