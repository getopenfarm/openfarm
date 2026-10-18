# Deploying OpenFarm

```
mkdir openfarm && cd openfarm
# copy docker-compose.prod.yml (as docker-compose.yml) and .env.prod.example (as .env) here
docker compose up -d      # http://<host>:8888, log in with the ADMIN_* values from .env
```

- **Upgrade:** `docker compose pull && docker compose up -d` (migrations run at startup).
- **Backup:** `./data` (part files, camera snapshots, DataProtection keys) **and** the Postgres volume
  (`docker compose exec postgres pg_dump -U openfarm openfarm > backup.sql`). Losing `./data/keys`
  makes stored printer secrets undecryptable.
- **Single replica only** — migrations and background services run inside the API process.
- **Build locally:** `docker build -f deploy/Dockerfile -t openfarm:local .` from the repo root.
- **Release:** push a `X.Y.Z` tag → `.github/workflows/docker-publish.yml` pushes
  `ghcr.io/getopenfarm/openfarm` (`X.Y.Z`, `X.Y`, `sha-*`, `latest`).
