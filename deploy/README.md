# Deploying OpenFarm

```
mkdir openfarm && cd openfarm
# copy docker-compose.prod.yml (as docker-compose.yml) and .env.prod.example (as .env) here
docker compose up -d      # http://<host>:8888, log in with the ADMIN_* values from .env
```

- **Upgrade:** `docker compose pull && docker compose up -d` (migrations run at startup). Replace the existing application instance; do not run an overlapping instance against the same database. Expect brief downtime.
- **Backup:** `./data` (part files, camera snapshots, DataProtection keys) **and** the Postgres volume
  (`docker compose exec postgres pg_dump -U openfarm openfarm > backup.sql`). Losing `./data/keys`
  makes stored printer secrets undecryptable.
- **Single replica only** — run exactly one OpenFarm application instance per installation, with PostgreSQL as a separate service. Migrations, printer connections, production dispatch, and part-processing workers run inside the API process. Do not use `docker compose up --scale openfarm=2`, rolling deployments with overlapping instances, or multiple application hosts sharing the database. This is an operating constraint, not a distributed lock enforced by the application.
- **Browser connectivity:** the built frontend uses same-origin `/api` and `/hubs` URLs, including file downloads. Access it using the server's hostname/IP; no frontend URL configuration is needed. Development uses Vite's existing API/WebSocket proxies. `VITE_API_BASE_URL` is an optional build-time override for custom frontend builds, not a runtime Compose setting. Empty values use the current origin.
- **Asset caching:** the HTML shell and unversioned public files revalidate (`no-cache`); fingerprinted Vite assets under `/assets/` cache for one year with `immutable`. Client routes support direct navigation; missing assets and unknown API/hub/health endpoints retain real error responses.
- **Build locally:** `docker build -f deploy/Dockerfile -t openfarm:local .` from the repo root.
- **Release:** push a `X.Y.Z` tag → `.github/workflows/docker-publish.yml` pushes
  `ghcr.io/getopenfarm/openfarm` (`X.Y.Z`, `X.Y`, `sha-*`, `latest`).
