---
title: Local development
description: Run the API, web application, and standalone documentation site from the repository.
---

The repository contains `api/` (ASP.NET Core), `web/` (React/Vite), `docs/` (this Docusaurus site), and `dev-notes/` (engineering notes). The docs site builds independently of the farm application.

## Run the application

Install the .NET SDK pinned in `api/global.json`, Bun, Bash, and PostgreSQL. Follow the root [README](https://github.com/getopenfarm/openfarm/blob/main/README.md) to create/configure the database or start its Compose service.

From the repository root:

```bash
dotnet restore api/OpenFarm.slnx
./dev.sh setup
./dev.sh up
```

`setup` installs web dependencies, provisions slicer bundles, and ingests profiles. API startup/ingestion applies migrations and initial seeding automatically. `dev.sh` does not load `.env` files; use `ConnectionStrings__Default` in the shell if the development database differs from the bundled settings.

The web app is at `http://localhost:5173`, with the HTTP API at `http://localhost:6868`. Development seed defaults are `admin` / `admin-openfarm` unless configured before account creation.

:::caution Development reset destroys data
`./dev.sh reset` immediately drops the configured database's public schema and deletes local application data, keys, slicers, and web dependencies. Do not use it to troubleshoot a farm you need to preserve.
:::

## Run the documentation

From the repository root:

```bash
cd docs
bun install --frozen-lockfile
bun run start
```

Use Node.js 20 or later. Docusaurus defaults to `http://localhost:3000`. Production verification:

```bash
bun run typecheck
bun run build
bun run serve
```

Local search is generated during the production build. Use the served build to test search; the development server alone does not create its index.

## Contribute

Edit user guides in `docs/content/` and their order in `docs/sidebars.ts`. Keep content grounded in actual UI/endpoints. Engineering designs, protocol details, and future plans belong in `dev-notes/`.

Application checks remain separate: run `bun run test`, `bun run lint`, and `bun run build` from `web/`, and the relevant .NET checks for backend changes. Do not hand-edit generated API clients.
