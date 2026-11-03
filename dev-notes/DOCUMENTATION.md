# Documentation site: content decisions and source map

The public-facing Docusaurus source lives in `docs/`. The former `docs/` notes,
including `superpowers/plans/`, moved to `dev-notes/` with their relative layout
preserved. They remain engineering references and are not published by Docusaurus.

## Research and information architecture

[Printago's documentation](https://docs.printago.io/docs) informed the reading
flow: quick start first, orientation, connection/setup, task-oriented workflows,
administration, API, troubleshooting, and support. Its product claims, account
model, cloud integrations, file restrictions, and screenshots were not reused.

OpenFarm's implemented navigation (`web/src/app/layout/nav-config.ts`) and routes
(`web/src/app/routes.tsx`) determine the actual scope. The quick start uses direct
whole-plate printing; exact units and assembly sets are explained in Runs, while
the assembly's direct Print dialog uses per-component full plates. The docs
explicitly separate saved requirements, printer compatibility, and availability.

The visual system comes from `web/src/index.css`, `Logo.tsx`, and
`UI_CONVENTIONS.md`: Geist/Geist Mono, zinc surfaces, restrained blue, compact
navigation, subtle borders, 10px corners, light/dark themes. Docusaurus retains
its accessible navigation, theme switch, TOC, previous/next links, and mobile
sidebar. Fonts are bundled locally. Local search has no external service/key.

## Implementation evidence

Check these sources when updating a guide; the notes alone may predate later code.

| Guides | Primary code and supporting notes |
| --- | --- |
| Installation/configuration/backups | `deploy/docker-compose.prod.yml`, `deploy/.env.example`, `deploy/entrypoint.sh`, `api/src/OpenFarm.Api/Program.cs`, `deploy/README.md`. The supplied deployment uses named volumes, not `./data` bind mounts. The guide adds the loopback PostgreSQL port missing from the current source Compose host-network layout. |
| Supported hardware and printer setup | `api/src/OpenFarm.Domain/Printers/PrinterModelCatalog.cs`, `PrinterModel.cs`, `web/src/features/printers/PrinterFormPage.tsx`, printer detail components, `SNAPMAKER_U1.md`, `BAMBU_LAN_PRINTING.md`. No implied support for uncataloged models. |
| Filament, spools, feed paths | `web/src/features/filament/`, `web/src/features/spools/`, `web/src/features/printers/components/FeedUnitsCard.tsx`, `FILAMENT_PROFILE_ASSIGNMENTS.md`, `FEED_UNIT_RELOADERS.md`, `FILAMENT_DEDUCTION.md`, `JOB_MATCHING.md`. |
| Parts, profiles, assemblies | `PartFormPage.tsx`, `PartDetailPage.tsx`, `usePartFileProcessing.ts`, `ProfilesImportPage.tsx`, `web/src/features/assemblies/`, upload endpoint size limits, `FILE_ESTIMATION_SETTINGS.md`, `OBJECT_SKIPPING.md`. |
| When profiles apply | `PROFILE_BEHAVIOR_AUDIT.md` records file classification, library estimation vs production slicing, pre-sliced validation, profile resolution, source research, and verification limits. Editable flowchart source is in `diagrams/profile-production-flow.dot`. |
| Queue, runs, matching, backfill, recovery | `NewProductionRunPage.tsx`, `QueuePage.tsx`, `PrintJobDetailPage.tsx`, `components/SendToQueueDialog.tsx`, `QueueJobActions.tsx`, `BackfillManager.tsx`, `JobMatchingDialog.tsx`, `JobObjectsCard.tsx`, production planner/domain, `PRODUCTION_DEMAND.md`, `JOB_MATCHING.md`, `OBJECT_SKIPPING.md`. |
| Dashboard, maintenance, Stats | `web/src/features/dashboard/`, `web/src/features/maintenance/`, `web/src/features/stats/`, `api/src/OpenFarm.Infrastructure/Reports/StatsReadModel.cs`, `MAINTENANCE.md`, `STATS.md`. Maintenance warns without blocking dispatch; Stats currently sums completed plate quantities for Units made. |
| Cost catalog, pricing, settings | `web/src/features/cost-catalog/`, `web/src/features/assemblies/`, `web/src/features/settings/`, `COST_CATALOG.md`, `PRINTER_COSTS.md`, `PRICING.md`. These are estimates, not inventory/sales accounting. |
| Accounts and API | `web/src/features/accounts/`, `api/src/OpenFarm.Api/Endpoints/Auth/`, `PersonalAccessTokenService.cs`, `PersonalAccessTokenHandler.cs`, `AUTHENTICATION.md`, `api/openapi.json`. Includes personal tokens merged into main before this work. |
| Native development | Root `README.md`, `dev.sh`, `api/global.json`, `web/package.json`, `infra/docker-compose.yml`. The docs site is independently installable/buildable. |

## Maintenance and verification

- Follow `docs/WRITING_GUIDE.md` for clear technical English guided by ASD-STE100.
  Keep necessary domain terms and preserve exact technical behavior. This is an
  editorial policy, not a claim of full standard compliance.
- `docs/sidebars.ts` is explicit to make category and previous/next order stable.
- Only `docs/content/` is published. Notes and specs are not indexed by search.
- The dedicated docs workflow installs the frozen Bun lock, typechecks, and
  builds on docs changes. Docusaurus rejects broken links and anchors.
- Test root and subpath builds when changing routing, assets, or search config.
- Local search indexes are generated by a production build, not `start` alone.
- Keep browser visual checks pending user verification, following `AGENTS.md`.
- No production hosting or application runtime routes are changed by this PR.
