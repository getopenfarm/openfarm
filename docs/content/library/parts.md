---
title: Parts and files
description: Upload, review, organize, and prepare part files for production.
---

Parts hold the files and requirements used to create jobs. Start from **Parts → Add part**.

## Supported uploads

The upload UI accepts **STL**, **3MF** (including sliced `.gcode.3mf`), and raw **`.gcode` / `.gco` / `.g`**. The request body limit is 500 MiB; allow for multipart overhead. A file extension alone does not guarantee readable metadata or compatibility with every printer.

Use a file prepared for the intended printer; raw G-code cannot safely become a different machine's toolpath just by changing a profile.

## Upload and review

1. Upload or drop a file and choose its name/tags.
2. Wait for background processing. Review the extracted plates, material requirements, nozzle, quantity, and estimates.
3. Select the plates to save for a multi-plate file.
4. Complete missing requirements and choose each line's **Production filament**.
5. Confirm the review to save the part records.

Sliced 3MF and G-code use embedded estimates. Unsliced project 3MF can estimate from embedded settings and supported named presets. STL and geometry-only 3MF can be reviewed and saved without estimates. Unknown time or grams remains unknown, not zero.

## Complete production requirements

An **Incomplete** part can be kept in the library while you fill gaps. **Ready to print** indicates complete part requirements, while actual dispatch still depends on printer matching.

Recognized object quantities are read-only. For files with unknown quantities, manual whole-plate values do not establish object-skipping capability. Exact quantities require verified native object identities; see [Outcomes and recovery](../production/outcomes.md).

## Edit, replace, or reprocess

Edit saved metadata in place and save the draft once. File replacement and reprocessing have their own review flows. They retain the current slicer pin unless you explicitly select another slicer. Changing the farm default does not update existing parts.

Finite queued jobs retain their captured production inputs. Changing a part does not rewrite running jobs or recorded material consumption.

## Organize the library

Use folders, tags, search, and the **Details / Thumbnails** switch. Move items using **Move to** or supported drag-and-drop targets; folders are not manual sort positions. Only empty folders can be removed. Folder organization does not change assembly references.
