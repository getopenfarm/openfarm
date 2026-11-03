---
title: When profiles apply
description: Follow STL, geometry-only 3MF, project 3MF, sliced 3MF, and G-code from library processing to production, and see which profiles are applied or only checked.
---

**The first question is whether OpenFarm needs to generate toolpaths.** STL and unsliced 3MF need slicing. Supported sliced 3MF and raw G-code already contain toolpaths, so changing a profile does not regenerate them.

There are two separate stages:

1. **Library processing** reads the uploaded file and, when supported, calculates estimates. It does not select settings from your connected printers or loaded spools.
2. **Production file preparation** checks the destination printer and its loaded spools. If the file needs slicing, OpenFarm generates a new file using that printer's configuration and the part's pinned slicer.

A successful estimate or **Ready to print** part does not guarantee that a particular printer can take the job. [Printer matching](../production/matching.md) still applies.

## What the three profiles mean

| Profile | Where you configure it for production | Role when slicing |
| --- | --- | --- |
| **Printer / machine** | Edit the destination printer → machine profile | Supplies the slicer's machine settings, including machine G-code and nozzle configuration. OpenFarm separately checks the registered hardware and currently confirmed nozzle. |
| **Process** | Edit the destination printer → process profile | Supplies the process preset, such as layer height, walls, infill, and support defaults. |
| **Filament** | Edit the filament → assignment for the printer's brand/model and nozzle size, with an optional color override | Supplies material settings for the tracked spool selected for each required material/color line. |

A printer record is physical hardware. A machine profile is a slicing preset. Likewise, a spool is physical stock, while a filament profile is a preset. Selecting a profile does not change the physical nozzle, plate, or material.

## Which kind of file did I upload?

The `.3mf` extension alone does not answer this question. A 3MF archive can contain geometry, project settings, or sliced toolpaths. A project can also retain geometry and settings after slicing.

| File contents | OpenFarm's route |
| --- | --- |
| **STL** | Unsliced geometry; needs production slicing. |
| **Geometry-only 3MF** (sometimes called plain 3MF) | No supported plate G-code and no recognized embedded project settings; needs production slicing. |
| **Unsliced project 3MF** | No supported plate G-code, but contains project settings; needs production slicing even if OpenFarm successfully estimates it. |
| **Sliced 3MF**, often named `.gcode.3mf` | Contains a supported `Metadata/plate_*.gcode` entry. OpenFarm treats the upload as already sliced, even if it also contains project settings and models. |
| **Raw `.gcode`, `.gco`, or `.g`** | Already sliced. |

OpenFarm inspects a 3MF archive for supported plate G-code; it does not decide from the `.gcode.3mf` filename alone. Other vendors' 3MF layouts are not automatically supported. Malformed or unsupported files can be rejected, and unreadable or conflicting metadata can leave requirements or estimates unknown.

If an archive mixes sliced and unsliced plates, the presence of supported plate G-code selects the already-sliced route for the upload. OpenFarm does not automatically slice the remaining plates through that route; a requested plate must have usable sliced output.

For background on the format, Prusa's [Saving projects as 3MF](https://help.prusa3d.com/article/saving-projects-as-3mf_1773) explains why a project can carry settings and modifiers as well as geometry. OpenFarm's own supported entries determine its behavior.

## During upload, replacement, or reprocessing

**The fleet's machine/process selections and inventory's filament-profile assignments are not used to calculate library estimates.** There is no farm estimation-profile fallback to configure.

| Uploaded file | Where library time/weight estimates come from | Are production profiles applied? |
| --- | --- | --- |
| STL or geometry-only 3MF | No automatic slicing estimate. Complete missing requirements during review; estimates remain unavailable. | No. |
| Supported unsliced project 3MF | OpenFarm first runs the selected slicer with the project's embedded settings. See the restricted retry below. | No; the destination printer and loaded spools are not selected here. |
| Sliced 3MF or raw G-code | Metadata already present in the file. OpenFarm does not run the slicer to recalculate it. | No. |

Missing time or weight stays unknown. It does not become zero, and an estimate failure does not by itself prevent saving a reviewable part. Production still needs complete requirements.

### Project settings and the named-preset retry

Embedded-project estimation is supported for recognized OrcaSlicer/Bambu Studio projects with usable settings and an available selected engine/version. Bambu Studio **2.7.1 on macOS** is explicitly excluded from this estimation mode. Prusa/Cura project settings are not supported by this estimator.

The initial invocation loads the project without external machine, process, or filament preset files. If the slicer rejects the project settings with a recognized validation/version/modified-machine error, OpenFarm can make a second attempt:

1. Read the machine, process, filament preset names, and build plate recorded in the project.
2. Look up each preset by name in the **selected engine's exact version catalog**. Each name must have exactly one match, the plate must be recognized, and the resulting set must pass compatibility checks.
3. Retry using those catalog presets. This replaces the project's global preset settings; it does not reproduce its custom preset edits. Project geometry and local settings remain inputs to the slicer.

The retry currently requires **one distinct nonblank filament preset name**; several slots may share it. A project with several distinct filament presets can still succeed on the initial embedded-settings attempt, but cannot use this retry. Missing/ambiguous presets or another slicing failure leave the estimate unavailable. This retry never selects your fleet's production profiles and is not a fallback used by production dispatch.

### Estimating does not make the source file pre-sliced

For an unsliced upload, the estimator's generated toolpath is temporary. OpenFarm keeps estimates and thumbnails, not that toolpath as the production file. The stored source still needs slicing when a job is prepared for a printer.

## When a job is prepared for a printer

Direct part printing, assembly printing, finite runs, and continuous backfill create queue work. They use the same production file-resolution and dispatch checks. Pinning a job restricts its destination; it does not bypass those checks.

![Decision tree: check whether the source already contains toolpaths, validate the printer and loaded filament assignments, then either prepare the existing toolpaths or resolve all three profiles and slice a new file.](../../static/img/profile-production-flow.svg)

### STL and unsliced 3MF: generate a new production file

For each destination printer, OpenFarm:

1. Checks complete part/job requirements, current nozzle confirmation, a compatible machine profile, and matching tracked spools on active feed paths.
2. Selects each required spool's filament-profile assignment for that hardware/nozzle group. A color override wins over the filament default. A part/job profile constraint must agree with the assignment; it does not choose a replacement preset. Spools in storage or on another printer are not fallbacks.
3. Requires the destination printer's **machine profile, process profile, and build plate**, plus the part/job's **exact slicer engine/version pin**.
4. Resolves the selected presets and their inheritance into that pin's catalog, validates the resolved set, and supplies it to the slicer. Each required filament line is checked separately, including compatibility with the selected plate's bed temperature.
5. Slices the part's selected project plate. Rechecks current configuration and stock before sending. If a check or slicing fails, nothing is sent.

| Setting source | Used to generate the production file? |
| --- | --- |
| Destination printer's machine profile | **Yes**, after resolution and validation. |
| Destination printer's process profile | **Yes**, after resolution and validation. |
| Destination printer's selected build plate | **Yes**; OpenFarm injects that plate selection into the process settings. |
| Selected tracked spools' effective filament-profile assignments | **Yes**, after resolution and validation, for each required line/slot. |
| Project's global machine/process/filament preset settings | They are not the production preset selection. OpenFarm loads the resolved external presets over the project. |
| Project geometry, placement, painting, modifiers, and object/part settings | The project is still passed to the slicer. OpenFarm does not strip these to make the file geometry-only. Local overrides can affect the result. |

The OrcaSlicer/Bambu Studio CLI loads external machine/process settings and filament files with priority over project-level settings. That does **not** mean every local override in a project is erased. Do not assume selecting a process preset removes an object's custom infill or support settings. The upstream [OrcaSlicer 2.3.1 CLI implementation](https://github.com/OrcaSlicer/OrcaSlicer/blob/v2.3.1/src/OrcaSlicer.cpp#L6099-L6103) and [Bambu Studio 2.7.1 CLI implementation](https://github.com/bambulab/BambuStudio/blob/v02.07.01.57/src/BambuStudio.cpp#L8257-L8260) describe the global loading priority.

For multi-color projects, OpenFarm matches project slots to job lines by the detected material/color, then loads each line's resolved filament profile in slot order. Slots known to be unused on that plate need no separate line. An unmatched used slot blocks the job; OpenFarm does not silently keep an embedded filament preset for it. STL/geometry-only files without project slots cannot supply several independent filament lines this way.

Production profile resolution supports **OrcaSlicer and Bambu Studio**. A SuperSlicer CLI adapter exists, but its scalar-only invocation does not implement these profile catalogs; it is not an alternative route around a missing or incompatible profile.

### Sliced 3MF and raw G-code: keep the existing toolpaths

OpenFarm does **not** re-slice these files or apply the destination printer's profiles to their toolpaths.

| Configuration | What happens for an already-sliced job? |
| --- | --- |
| Machine profile | **Required and validated** against registered hardware and the currently confirmed nozzle, as part of selecting compatible loaded filament. Its settings are not loaded into a slicer or applied to the toolpaths. |
| Process profile | **Not required for this route and not applied** to the toolpaths. |
| Filament-profile assignments | **Required and validated** for the tracked loaded spools, including material/nozzle/machine compatibility and any narrower profile constraint. Their temperatures, flow, cooling, etc. do not replace values in the toolpaths. |
| Build plate | No new slicing-plate selection or plate-temperature validation is performed by the slicing-profile resolver. Prepare the file for the physical plate you will use. |
| Part's slicer pin | Not needed to run a slicer for this route. Production does not map these validation assignments into a pinned slicer catalog. |

For real supported printers, the file must declare a recognizable **target printer model matching the destination**. A missing/unrecognized target or another model blocks dispatch. The simulator is exempt from this model check. Filling a missing nozzle/material requirement manually does not fill in a missing printer-model declaration inside the sliced file.

The existing slicing temperatures, speeds, layer height, supports, and other toolpath choices remain those of the original slice. Choosing another spool color can change physical stock matching; it does not rewrite the file's slicing settings.

:::note Existing toolpaths can still need execution preparation
Keeping toolpaths does not promise byte-for-byte identical uploads. Supported exact partial plates/object skipping can prepare an execution artifact, and printer adapters can package or adapt the file for the protocol. These operations are separate from applying slicing profiles. See [Outcomes and recovery](../production/outcomes.md).
:::

### Original downloads, job downloads, and printer-started prints

Downloading a **part's original file** returns the stored upload without applying profiles. It does not export the temporary library-estimation result.

A **job file** download returns its retained execution artifact when one is available. Otherwise, it uses the same production file preparation described above and needs an assigned or pinned target printer. Downloading a retained artifact does not re-slice it with today's profiles.

A print started outside OpenFarm can be observed as a printer-started print. Observation does not run it through OpenFarm's queue slicing or apply OpenFarm's profile choices to it.

## Which version of a profile is used?

This section applies when OpenFarm **slices** a production file. A saved profile choice may come from a different catalog than the part's pin:

| Saved choice | Resolution for production slicing |
| --- | --- |
| System preset already in the pinned engine/version | Use that preset. |
| System preset from another version of the same engine | Map to the pinned catalog by vendor and external preset ID, tolerating a rename. Without an ID, use vendor/name. |
| System preset from the other supported engine | Require an exact twin in the pinned catalog by external ID, vendor, and name. A similar display name is insufficient. |
| User preset native to the pinned engine | Use the user preset; resolve its system ancestors to the pin's catalog. |
| User preset from another engine | Block. |

Missing/ambiguous matches, missing catalogs, unresolved inheritance/templates, unavailable slicers, and incompatible resolved presets block slicing. OpenFarm does not choose a nearby preset, another engine, or a newer version to make a job proceed.

## What happens after I change something?

| Change | Effect |
| --- | --- |
| Change the farm default slicer | Changes default processing choices and picker catalogs. Existing part/job pins do not move. |
| Replace or reprocess an unsliced part | Keeps its current pin unless you explicitly choose another slicer; a part without a pin can take the farm default. Re-reads requirements/estimates using the applicable file route above. |
| Reprocess a sliced file | Reads its metadata again. Does not regenerate toolpaths with your current profiles. |
| Change printer machine/process/plate settings or a filament assignment | Used/checked on the next production preparation according to the route above. Does not recalculate saved library estimates. |
| Change settings or loaded spools while a file is being generated | Dispatch checks fresh state again. A configuration mismatch stops the send and leaves the job for retry. |
| Edit a library part after finite jobs were queued | Finite jobs retain their captured source inputs and pin. Live printer settings and spool assignments are still checked at dispatch. Continuous follow-latest work can use the updated part before dispatch. |

**To preserve the slicing choices of an external slicer, upload its supported sliced output. To have OpenFarm generate toolpaths with your destination printer's configured profiles, upload STL or an unsliced supported 3MF.** Either route still needs valid hardware, material, and matching configuration.

Use **Job diagnosis** to see why a particular printer cannot take a job. After production slicing, the job's recorded slicer and resolved profiles describe that production slice; the library estimate may have used different settings.
