---
title: Quick start
description: Connect a printer, load tracked filament, and send your first OpenFarm job to the queue.
slug: /
---

Get from an empty farm to your first queued print. OpenFarm runs on your own server and connects to printers over your local network.

## Before you begin

- A running OpenFarm installation. Start with [Docker installation](installation/docker.md) if you do not have one.
- An account with permission to add printers, manage filament and parts, and create print jobs. The first administrator can complete this walkthrough.
- A [supported printer](printers/supported-printers.md) reachable from the **OpenFarm server**.
- A small, known-good sliced 3MF made for that printer and nozzle, plus the matching physical filament.

## 1. Sign in

Open your installation's address, normally `http://<server-ip>:6868`. Use the administrator username and password configured at first startup. If no password was configured, the initial defaults are `admin` / `admin-openfarm`.

Change that password in **My profile → Security**. There is no public sign-up flow; an administrator creates additional accounts in **Users & groups**.

## 2. Connect a printer

Go to **Printers → Add printer**. Select a discovered printer or enter its connection details manually. Choose a name and supply the credential requested for the model. Adding the printer checks the connection before saving it.

For model-specific preparation, follow [Bambu Lab](printers/bambu.md) or [Snapmaker U1](printers/snapmaker.md). Wait for the printer to become reachable and report its nozzle size.

Open the printer's **Edit** page. Select a compatible machine profile, process profile, and build plate, then save. Profile options come from the installed slicer catalog.

## 3. Register and load filament

1. Open **Filament → Add filament** and create the material, vendor, and color you will use.
2. Configure that filament's profile assignment for your printer model and nozzle. A color can inherit the filament's assignment or override it.
3. Use **Add spools** to register the physical spool and its remaining weight.
4. Physically load the spool, then use **Load spool** to associate it with the correct printer feed slot.

A printer-reported material does not replace a tracked spool. OpenFarm needs both the physical configuration and its inventory record. See [Filament catalog](filament/catalog.md) and [Spools](filament/spools.md).

## 4. Add a part

1. Go to **Parts → Add part**.
2. Upload the file.
3. Wait for background processing to finish.
4. Review the file information. For a multi-plate upload, review the plates you want to save.
5. Select the **Production filament** that matches the loaded spool's catalog color.
6. Complete any missing print requirements.

Save the part. **Ready to print** means its requirements are complete. OpenFarm checks printer compatibility separately. A missing estimate stays unknown. See [Parts and files](library/parts.md).

## 5. Send a plate to the queue

Open the part and choose **Print**. Request one plate, review the plan, and choose **Send to queue**. A production run is optional for this first print.

Open **Queue** to follow the job. If it cannot start, open its status link or **Job diagnosis** on the job page. [Printer matching](production/matching.md) explains each requirement and where to fix it.

## 6. Clear the bed and mark ready

Check that the physical bed is empty, the correct plate is installed, and the printer is safe to start. If the printer is awaiting clearance, use **Mark ready**. A connected, idle printer with compatible loaded material can take queued work automatically.

:::caution Automatic dispatch
Sending work to the queue can start a compatible ready printer. Finish the physical checks before queueing work or marking a printer ready.
:::

## 7. Finish the cycle

Monitor the job from **Queue**, the printer page, or **Dashboard**. After printing, inspect the output, remove it from the bed, and mark the printer ready for the next job. If output needs confirmation or replacement, follow [Outcomes and recovery](production/outcomes.md).

## Next steps

- [Production runs](production/runs.md) track an order or batch with fixed quantities.
- [Assemblies](library/assemblies.md) define products with multiple printed components.
- [Maintenance](operations/maintenance.md) tracks service intervals.
- [Troubleshooting](troubleshooting.md) helps with connection and dispatch problems.
