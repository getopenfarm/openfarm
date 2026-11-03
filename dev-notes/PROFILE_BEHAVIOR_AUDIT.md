# Profile behavior documentation audit

Reviewed against OpenFarm `d433b84e0c54285b67883867a7e0edaf2134b8a3`
(the main branch containing the Docusaurus site), 2026-11-03. This change documents
behavior; no application code, test expectations, dependencies, or slicer pins changed.

## Placement

Add `docs/content/library/profile-behavior.md` as **When profiles apply** immediately
after **Profiles and slicers** in the existing **Parts & profiles** category.
The existing profiles page introduces configuration/import; the new page owns the
cross-workflow decision logic. No new top-level category is needed. Link from
parts, profiles, filament assignments, printer configuration, and matching.

The flowchart is a static SVG, with its editable Graphviz source under
`dev-notes/diagrams/`. It needs no Docusaurus plugin or browser runtime dependency.
The page's tables and ordered steps provide the decision logic in text as well.

## Source map

Paths are relative to the repository root. The implementation takes precedence
over historical specs and notes.

| Claim | Implementation and existing tests inspected |
| --- | --- |
| STL always needs slicing; raw G-code never does; supported plate G-code selects the already-sliced 3MF route | `api/src/OpenFarm.Infrastructure/Parts/PartFileClassifier.cs`; `PartFileClassifierTests` |
| Embedded-project detection, metadata completeness, unsupported Prusa/Cura settings | `UnslicedProjectMaterialResolver.cs`, `ProjectSettingsMetadata.cs`; `EmbeddedMetadataTests` |
| Geometry has no estimate fallback; already-sliced files read metadata; embedded project first attempt | `PartFileProcessor.ResolveMaterialAsync`, `ResolveEstimateAsync`; `PartFileProcessorTests`, `LibraryFallbackStlEstimateTests` |
| Project named-preset retry is limited to rejected project settings, one distinct filament name, exact catalog/name lookups | `PartFileProcessor.ResolveEstimateAsync`, `SlicerCliExitCodes.RejectsProjectSettings`, `ResolveProjectEstimationProfiles.cs`; `ResolveProjectEstimationProfilesTests` |
| Production first resolves hardware-compatible tracked active loaded spools and their assignments, including for pre-sliced input | `ProductionProfileReadiness.ResolveAsync`, `RepresentativeFilamentResolver.ResolveLoadedAsync`, `ProfileCompatibility.cs`; `RepresentativeFilamentResolverTests` |
| Process/plate/pinned catalog resolution only occurs for a file needing slicing | `ProductionProfileReadiness.ResolveSettingsAsync`; `JobFileResolver.ResolveAsync` |
| Pre-sliced target model must match real printer; simulator exemption | `ProductionProfileReadiness.EnsureSlicedForPrinterAsync`; `ProductionSlicedModelGuardTests` |
| Slicing uses destination machine/process/plate and each loaded filament's resolved preset | `ResolveSlicingProfileSelection.cs`, `PrusaSliceFamilyInvocation.cs`, `JobFileResolver.cs`; `SlicerInvocationTests`, `StrictSlicingDispatchTests` |
| Project input is not reduced to geometry; project local settings are still slicer inputs | `CliSlicerEngine.SliceCoreAsync` copies the source; `JobFileResolver` supplies the same archive with external profiles. Orca's `PrintApply.cpp` applies model/object/region configurations separately from defaults. |
| Per-slot filament files, unused slots, selected plate | `JobFileResolver.FilamentSlots`, `ProjectSlotsAsync`; `JobFileFilamentSlotTests`, `JobFilePlateTests`, `RealSlicerMultiColourTests`, strict-dispatch S13/S14 |
| Strict profile identity and engine/version mapping; no substitution on production failure | `ProfileResolver.cs`, `ResolveSlicingProfileSelection.cs`; `ProfileResolverTests`, `ProfileResolverInvariantTests`, `RealSlicerStrictResolutionTests` |
| Pin selection and preservation; finite snapshots and follow-latest | `ResolveSlicerSelection.cs`, `AddPartCommandHandler.cs`, `ReplacePartFileCommandHandler.cs`, `ProductionSnapshotFactory.cs`, `ProductionJobRequirements.cs`; selection/pin tests and strict-dispatch S4/S12 |
| Fresh configuration stamp and stock recheck before send | `ProductionDispatchBackgroundService.DispatchToLiveIntegratedPrinterAsync` and `ProductionProfileReadiness.StampAsync`; strict-dispatch S8 and failure scenarios |
| Sliced input may still be packaged/adapted for execution | `JobFileResolver.PrepareObjects`, `ObjectExecutionArtifact`, printer adapter preparation; do not promise byte-identical uploads |
| Original-file vs production-file downloads | `GetPartFileQueryHandler.cs`, `GetPrintJobFileQuery.cs` |
| SuperSlicer adapter is not a production catalog alternative | `ProfileEngineCompatibility.cs`, `ProfileResolver.cs`, `SuperSlicerInvocation.cs`; profile-resolution and invocation tests |

Most files named without a full path above are in `api/src/OpenFarm.Application/Parts/`,
`Profiles/`, `Production/`, or `api/src/OpenFarm.Infrastructure/Parts/`, `Production/`.
Test namespaces mirror those directories.

## Internet research

Primary upstream sources were read before drafting, with additional source tracing
while reviewing the draft. Upstream format/CLI behavior is context, not evidence
for OpenFarm-specific configuration gates.

- [Prusa: Saving projects as 3MF](https://help.prusa3d.com/article/saving-projects-as-3mf_1773): explains geometry plus settings/modifiers in a project archive. Does not establish OpenFarm's supported archive layout.
- [OrcaSlicer CLI documentation](https://github.com/OrcaSlicer/OrcaSlicer/wiki/cli_misc): external machine/process and filament loading flags. The wiki tracks newer releases, so versioned source was also checked.
- [OrcaSlicer 2.3.1 CLI](https://github.com/OrcaSlicer/OrcaSlicer/blob/v2.3.1/src/OrcaSlicer.cpp#L2406-L2742): external preset loading and global configuration update paths; loading-priority help is at lines 6099–6103.
- [OrcaSlicer 2.3.1 PrintApply](https://github.com/OrcaSlicer/OrcaSlicer/blob/v2.3.1/src/libslic3r/PrintApply.cpp): distinct global defaults, model object configuration, layer ranges, and region configuration; external presets do not imply removing local overrides.
- [Bambu Studio 2.7.1 CLI](https://github.com/bambulab/BambuStudio/blob/v02.07.01.57/src/BambuStudio.cpp#L8257-L8260): same documented global loading priority in the bundled version's source.

## Verification results

- Docs frozen dependency install, TypeScript check, and production build succeeded.
  Docusaurus checked internal Markdown links/anchors and packaged the SVG. No
  dependencies or build configuration were added.
- Focused Application tests: **126 passed**, no skips/failures. Covers profile and
  slicer selection/resolution, loaded-spool assignments, target-model guard, and
  dispatch provenance.
- Focused Infrastructure tests: **59 passed, 1 failed**, no skips. Includes real
  OrcaSlicer 2.3.1 toolpath/golden checks, cross-engine catalog resolution and
  multi-color Bambu/Snapmaker profile loading, plus classifier/parser/invocation
  and slot/plate tests.
- Focused API integration tests: **18 passed**, no skips/failures. Includes all
  `StrictSlicingDispatchTests` and `LibraryFallbackStlEstimateTests`: production
  preset selection, pre-sliced bypass, missing presets/pins/catalogs, configuration
  change during slicing, no production fallback, and no fleet estimate fallback.
- `git diff --check` passed.
- Browser visual checks remain pending user verification under `AGENTS.md`.

### Existing real-slicer golden expectation mismatch

`OrcaSlicer231_EmbeddedProject_PreservesItsSavedSettings` expects **11.09 g**;
the installed Linux OrcaSlicer 2.3.1 produces **11.36 g**. No test or production
code was changed by this documentation work.

A separate invocation of the same source project with exactly the embedded-project
flags produced 35m58s and 11.36 g. The output's G-code header, slice-info plate
weight, and filament `used_g` all agree on 11.36 g. Comparing input and output
`Metadata/project_settings.config` found the same values for every original
setting except equivalent thumbnail-list whitespace:
`48x48/PNG,300x300/PNG` became `48x48/PNG, 300x300/PNG`.

This establishes that the discrepancy is in the existing exact numeric baseline
for this binary/run, rather than OpenFarm selecting a fleet preset for the
embedded estimate. It does not establish why that baseline differs. The test is
left failing and explicitly reported; the docs make no promise of identical
numeric estimates across slicer builds/platforms.

Reproduction: run the Infrastructure test project with filter
`FullyQualifiedName~RealSlicerGoldenFileTests.OrcaSlicer231_EmbeddedProject_PreservesItsSavedSettings`
using the installed 2.3.1 Linux bundle. The environment was configured with
`source /workspace/openfarm-setup/env.sh` so bundled native libraries were available.
