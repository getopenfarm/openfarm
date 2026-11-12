---
title: Local development
description: Run the API, web application, and standalone documentation site from the repository.
---

The repository contains these directories:

- `api/`: the ASP.NET Core backend.
- `web/`: the React application, built with Vite.
- `docs/`: this Docusaurus site.
- `dev-notes/`: engineering notes.

You can build the docs site independently of the farm application.

## Run the application

Install the .NET SDK version specified in `global.json`, Bun, Bash, and PostgreSQL. Follow the root [README](https://github.com/getopenfarm/openfarm/blob/main/README.md) to configure the database or start its Compose service.

From the repository root:

```bash
dotnet restore api/OpenFarm.slnx
./dev.sh setup
./dev.sh up
```

`setup` installs web dependencies and slicer bundles, then imports their profiles. API startup and profile import apply database migrations and create initial records automatically.

`dev.sh` does not load `.env` files. If your database uses different connection settings, set `ConnectionStrings__Default` in the shell.

The web app is at `http://localhost:5173`, with the HTTP API at `http://localhost:6868`. Development seed defaults are `admin` / `admin-openfarm` unless configured before account creation.

:::caution Development reset destroys data
`./dev.sh reset` immediately drops the configured database's public schema and deletes local application data, keys, slicers, and web dependencies. Do not use it to troubleshoot a farm you need to preserve.
:::

## Run the documentation

From the repository root:

```bash
./dev.sh docs
```

Use Node.js 20 or later and Bun. The command installs the frozen docs dependencies
and starts Docusaurus at `http://localhost:3000` with live reload. Additional options
pass through, for example `./dev.sh docs --port 3001 --no-open`.

Production verification from `docs/`:

```bash
cd docs
bun run typecheck
bun run build
bun run serve
```

Local search is generated during the production build. Use the served build to test search; the development server alone does not create its index.

## Contribute

Edit user guides in `docs/content/` and their order in `docs/sidebars.ts`. Describe the current interface and API behavior. Engineering designs, protocol details, and future plans belong in `dev-notes/`.

Application checks remain separate: run `bun run test`, `bun run lint`, and `bun run build` from `web/`, and the relevant .NET checks for backend changes. Do not hand-edit generated API clients.
