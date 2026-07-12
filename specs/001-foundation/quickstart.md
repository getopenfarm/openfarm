# Phase 0 Quickstart: Foundation

Validation guide for the Foundation feature once implemented — proves each user story
end-to-end. See `data-model.md` for entity shapes and `contracts/openapi.yaml` for exact
request/response bodies.

## Prerequisites

```bash
cp .env.example infra/.env   # adjust Seed:AdminEmail / Seed:AdminPassword if desired
cd infra && docker compose up   # postgres + api (dotnet watch, applies InitialFoundation migration + seeds) + web (bun run dev)
```

Wait for the API's health check to report ready:

```bash
curl -f http://localhost:8888/health/ready
```

## Scenario 1 — Seeded operator login (User Story 1)

```bash
curl -i -X POST http://localhost:8888/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username": "<Seed:AdminEmail value>", "password": "<Seed:AdminPassword value>"}'
```

**Expected**: `200`, body contains a `token`. Then:

```bash
curl -i http://localhost:8888/api/v1/auth/whoami -H "Authorization: Bearer <token>"
```

**Expected**: `200`, `organizationId` matches Scenario 2's org id.

Negative checks:
- Same login call with a wrong password → `401`, no token issued.
- `GET /api/v1/auth/whoami` with no `Authorization` header → `401`.

In the browser: visit `http://localhost:5173/`, confirm redirect to `/login`, sign in with
the seeded credentials, confirm landing on the authenticated shell.

## Scenario 2 — Single auto-created organization (User Story 2)

```bash
docker compose exec postgres psql -U openfarm -d openfarm -c 'select id, name from "Organizations";'
```

**Expected**: exactly one row, present immediately after first boot with no manual step.

Restart the stack three times (`docker compose restart api`) and re-run the query each
time — row count must stay at exactly 1 (SC-004).

## Scenario 3 — Connection secret encryption, masking, immutability (User Story 3)

```bash
curl -i -X POST http://localhost:8888/api/v1/connection-secrets \
  -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' \
  -d '{"printerSerialNumber": "AC12345", "plainAccessCode": "real-secret-value"}'
```

**Expected**: `201`, response body has `printerSerialNumber: "AC12345"` in full and
`maskedAccessCode: "***"` — never the plaintext.

```bash
docker compose exec postgres psql -U openfarm -d openfarm \
  -c 'select "PrinterSerialNumber", "EncryptedAccessCode" from "ConnectionSecrets";'
```

**Expected**: `EncryptedAccessCode` is ciphertext, not `real-secret-value`.

```bash
docker compose logs api | grep -i 'real-secret-value'
```

**Expected**: no matches (SC-003).

```bash
curl -i http://localhost:8888/api/v1/connection-secrets -H "Authorization: Bearer <token>"
```

**Expected**: listed entry shows the same masked shape — confirm there is no `PUT`/`PATCH`
route for this resource (SC-006) by checking the OpenAPI doc / attempting one and getting `404`/`405`.

## Scenario 4 — Activity log captures state changes (User Story 4)

```bash
curl -s http://localhost:8888/api/v1/activity-log -H "Authorization: Bearer <token>" | jq .
```

**Expected**: at least one entry with `entityType: "ConnectionSecret"`, `eventType`
indicating creation, `actorType: "Operator"`, `actorDisplayName` matching the seeded
operator, and a `timestampUtc` shortly after Scenario 3's create call. Entries are ordered
newest-first (FR-010).

Negative check: attempt `POST /connection-secrets` with an empty `plainAccessCode` →
expect `400` and confirm no new activity-log row is written for that failed attempt (FR-011).

## Scenario 5 — Base UI kit / ready screen (User Story 5)

In the browser, after logging in:
1. Navigate to the "Connection Secrets" nav item (part of `AppShell`'s nav array).
2. Click "Add credential" → a shadcn `Dialog` opens with a `react-hook-form` + `zod`-validated
   `form`/`input`/`label` set.
3. Submit → a `sonner` toast confirms success, the new row appears in the `table`, masked.
4. Attempt to submit with an empty access code → inline validation error, no request sent.

**Expected**: the entire screen was built from `src/components/ui/*` primitives with zero
new low-level CSS/styling code (SC-005) — confirm by checking the diff for
`ConnectionSecretsPage.tsx` and its `components/` (if any) contains no raw Tailwind utility
soup outside of composing existing primitives, no hand-rolled modal/table markup.

## Contract check

```bash
cd web && bunx orval && git diff --exit-code src/lib/api/generated
```

**Expected**: no diff — the committed generated client matches the current
`api/openapi.json`.

## Automated verification (run instead of manual curl once implemented)

```bash
dotnet test api/OpenFarm.slnx
cd web && bun run test && bunx tsc -b && bun run build
```
