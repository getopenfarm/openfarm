---
title: Parts and files
description: Upload, review, organize, and prepare part files for production.
---

Parts hold the files and requirements used to create jobs. Start from **Parts → Add part**.

## Supported uploads

You can upload **STL**, **3MF** (including sliced `.gcode.3mf`), and raw **`.gcode` / `.gco` / `.g`** files. The complete upload request has a 500 MiB limit. Leave space for the form data sent with the file. A supported file extension does not guarantee readable metadata or compatibility with every printer.

Use a file prepared for the intended printer; raw G-code cannot safely become a different machine's toolpath just by changing a profile.

See [When profiles apply](profile-behavior.md) for the file decision tree and the separate rules for library estimates, production slicing, and already-sliced jobs.

## Upload and review

1. Upload or drop a file.
2. Set its name and tags.
3. Wait for background processing to finish.
4. Review the extracted plates, material requirements, nozzle, quantity, and estimates.
5. For a multi-plate file, select the plates to save.
6. Complete missing requirements.
7. Choose **Production filament** for each material line: the file color (default), any color of the material, any color of one filament, or one color.
8. Confirm the review to save the part records.

For sliced 3MF and G-code, OpenFarm reads estimates stored in the file. For unsliced project 3MF, it can calculate estimates from embedded settings or supported named presets. You can review and save STL and geometry-only 3MF without estimates. Unknown time or filament weight stays unknown. It does not become zero.

## Complete production requirements

An **Incomplete** part can be kept in the library while you fill gaps. **Ready to print** indicates complete part requirements, while actual dispatch still depends on printer matching.

If OpenFarm recognizes the object quantity, you cannot edit it. For files with unknown quantities, you can enter a whole-plate quantity manually. That value does not enable object skipping. Exact quantities require verified object identifiers in the file. See [Outcomes and recovery](../production/outcomes.md).

## Edit, replace, or reprocess

Edit the saved details and save your changes. File replacement and reprocessing each have a separate review step. They keep the part's saved slicer and exact version unless you select another slicer. Changing the farm default does not update existing parts.

Finite queued jobs retain their captured production inputs. Changing a part does not rewrite running jobs or recorded material consumption.

## Organize the library

Use folders, tags, search, and the **Details / Thumbnails** switch. Move items with **Move to** or drag them to a supported destination. Folders organize records; they do not set a manual item order. You can remove only empty folders. Moving a part does not change the assemblies that use it.
