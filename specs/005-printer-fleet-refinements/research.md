# Phase 0 Research: Printer Fleet Management Refinements

All four user stories are refinements over code delivered in `specs/002-printer-integration/` and
`specs/004-virtual-printer/`; nothing here introduces a new subsystem, so research is scoped to
concrete design decisions rather than open technology choices. No `NEEDS CLARIFICATION` markers
remain in plan.md's Technical Context — the two ambiguities the spec itself flagged were already
resolved in spec.md's Clarifications section (status never blocks edit; a rejected zero-trust edit
gets no dedicated audit log entry).

## 1. Unified edit endpoint shape — decision

**Decision**: Replace `PUT /api/v1/printers/{id}/connection` with `PATCH /api/v1/printers/{id}`.
Request carries `name` (always required), and `ipAddress`/`accessCode` as an **optional pair** —
both present or both absent, never one without the other.

### 1a. `PATCH` vs `PUT` — decision

**Decision**: `PATCH`, despite every existing "edit" endpoint in this API (`EditPrinterConnectionEndpoint`
being replaced, plus `UpdateFilamentProfileRawDataEndpoint`/`UpdateMachineProfileRawDataEndpoint`)
using `PUT`.

**Rationale**: `PUT`'s REST contract is idempotent full-resource replacement — a client is expected to
be able to resend the same complete representation and get the same result. That's structurally
impossible here: the access code is write-only (`ConnectionSecret` is `IDataProtector`-encrypted and
never decrypted back to the client), so a client can never know the printer's full current state well
enough to resend it. Combined with `name`-only edits deliberately omitting `ipAddress`/`accessCode`
entirely (research §2/§3 below), every request this endpoint accepts is a partial modification by
construction — exactly what `PATCH` means. Keeping `PUT` here would optimize for superficial
consistency with endpoints that don't share this constraint over correctly describing what the
request actually does.

**Alternatives considered**: *Keep `PUT` for consistency with the existing all-`PUT` "edit" endpoints
in this API* — rejected on correctness grounds once asked directly; noted here so a future reader
doesn't mistake the inconsistency for an oversight. If a broader "when do we use PATCH vs PUT" policy
question comes up later (e.g. for the two profile raw-data endpoints), it's a separate, larger
discussion out of scope for this feature.

**Rationale**: FR-003 requires a name-only change to skip the verify-before-save gate entirely, but
today's `EditPrinterConnectionRequest` requires `ipAddress`/`accessCode` unconditionally (both
`NotEmpty` in `EditPrinterConnectionCommandValidator`). A single request DTO has to let the caller
express "I'm only renaming" distinctly from "I'm also changing connection details." Making the pair
optional-together (rather than independently optional) avoids a half-specified connection change
reaching the verify call with a missing field — mirrors today's behavior where both fields are
supplied whenever connection is edited at all.

**Alternatives considered**:
- *Two endpoints (rename + connection edit) kept separate*: rejected — FR-001 explicitly asks for
  "one edit operation," and acceptance scenario 2 expects a single save action to cover both.
- *`ipAddress` optional, `accessCode` always required*: rejected — would force re-entering the access
  code on every rename even when connection is untouched, defeating FR-003's whole point.
- *Independently-optional `ipAddress`/`accessCode` (patch semantics per field)*: rejected as
  overengineered for this scope — nothing in the spec asks for changing IP without access code (the
  access code is write-only/never round-tripped to the client per existing secret-handling
  convention, so the UI can't safely default-fill it anyway); the paired-optional rule is simpler and
  matches the one real use case in the acceptance scenarios.

## 2. Detecting "connection changed" to gate verification — decision

**Decision**: The **presence of `ipAddress`+`accessCode` in the request** (not a diff against the
stored value) decides whether the verify-before-save gate runs. If both are omitted, the handler
calls `Printer.Rename(name)` only. If both are present, it re-verifies via
`IPrinterConnectorFactory`, then calls `Printer.Rename(name)` + `Printer.EditConnection(ipAddress)`.

**Rationale**: The server cannot diff the incoming `accessCode` against the stored one to detect "did
this actually change" — the stored value only exists as an `IDataProtector`-encrypted
`ConnectionSecret`, never decrypted for comparison outside the connector itself. Presence-based
detection sidesteps that entirely and matches how the frontend already decides whether to show a
"connection changed" state (research §7 below).

## 3. Zero-trust rejection of brand/model/serial mismatches (FR-010) — decision

**Decision**: Add three **optional** fields to the request DTO — `brand`, `modelCode`,
`serialNumber` — purely so a caller who supplies them gets checked, not silently ignored. In the
command handler, after loading the printer, compare each supplied field against the printer's
current value; on any mismatch, throw a new `ImmutablePrinterFieldMismatchException` before any
mutation happens. Map it in `DomainExceptionHandler` to `422 Unprocessable Entity`, alongside the
existing `UnknownPrinterModelException`/`PrinterConnectionVerificationFailedException` entries —
same mechanism, no new middleware.

**Rationale**: FastEndpoints/`System.Text.Json` silently drops JSON properties that aren't declared
on the request DTO. If the DTO simply omitted `brand`/`modelCode`/`serialNumber` altogether, a
direct-API caller sending them would have those fields silently discarded — which reads as "ignored,"
not "rejected," failing the letter of FR-010/acceptance-scenario-6 even though the fields would never
be *applied*. Declaring them as optional-but-checked fields is what makes the rejection observable
and intentional rather than an accidental byproduct of DTO shape. The product UI itself never
populates these three fields (spec assumption: only a bypassing direct-API caller would).

**Alternatives considered**: *Rely on JSON deserialization silently dropping unknown fields* —
rejected, doesn't satisfy "rejects the request outright" (FR-010) or the clarification's "validation
error only" resolution, which presupposes an error is actually raised.

## 4. Exposing the printer catalog (US3) — decision

**Decision**: Add `GET /api/v1/printers/catalog` returning every `(Brand, ModelCode)` pair currently
in `PrinterModelCatalog`, grouped by brand. No new table, no new persistence — a thin `Application`
query reads directly from the existing static dictionary in `OpenFarm.Domain.Printers`.

**Rationale**: `specs/002-printer-integration/data-model.md` already settled that `PrinterModelCatalog`
stays an in-code lookup rather than a database table, citing the constitution's "Build for Expansion,
Not Speculation" principle (no second real need for a `printer_profiles` table exists yet). US3 only
asks the frontend to stop *duplicating* the catalog, not to relocate its source of truth — an
endpoint that reads the existing in-code catalog satisfies FR-007/FR-008 (dialog options must match
what registration would accept) with zero schema change and zero risk of the read path drifting from
the write-side validation, since both sides call the same `PrinterModelCatalog`.

**Alternatives considered**: *Move the catalog into a database table now, seed it, expose it via a
generic CRUD-style read* — rejected as speculative for this feature's scope; nothing here needs the
catalog to be operator-editable at runtime, and constitution Principle IV explicitly argues against
pulling that extraction forward without a second real need.

## 5. Frontend catalog consumption (US3) — decision

**Decision**: `AddPrinterDialog.tsx` drops its inline `PRINTER_BRANDS`/`MODELS_BY_BRAND` constants and
fetches the new catalog endpoint via TanStack Query (through the Orval-regenerated hook). Brand/Model
`<select>` options are built from the fetched data; zod validation for brand/model moves from a
static `z.enum([...])` to a runtime check against the fetched list (e.g. `z.string().min(1)` plus a
`.refine()` closed over the loaded catalog), since a compile-time enum can no longer describe a
server-driven list. A loading state disables the selects until the catalog arrives; a fetch error
disables submission entirely (US3 AC3) rather than falling back to an empty or stale list.

**Rationale**: Directly implements FR-007 ("no frontend code change" to add a brand/model) and the
edge case explicitly called out in spec.md ("catalog fetch fails after a Brand is already selected
mid-form" → surface the error, disable submission).

## 6. SignalR printer-event contract audit (US4) — finding

**Finding**: Reviewed `FarmHub.cs`, `FarmHubPrinterEventPublisher.cs` (the three broadcast events —
`PrinterStatusChanged`, `PrinterTelemetryUpdated`, `CameraFrameReceived`), and the frontend consumer
`useFarmHub.ts`. **No brand- or model-specific field, event name, or branch exists** in the hub,
publisher, or frontend subscription code — every field (`status`, `isReachable`, `nozzleTempC`,
`bedTempC`, `printProgressPercent`, `currentStageLabel`, `frameJpegBase64`) is expressed in terms of
the canonical `PrinterStatus`/telemetry model, not a specific brand's protocol.

One naming choice is worth recording without action: `AmsUnit`/`AmsTray` (the multi-material-unit
telemetry shape) uses Bambu Lab's own product name ("AMS") for the concept, baked in since
`specs/002-printer-integration/data-model.md`. This is a **pre-existing naming choice**, not a hidden
branch introduced by this feature, and renaming it is out of scope per this story's own bounding
(spec Assumptions: "does not preemptively add abstractions for hypothetical future brands... only
fixes a concretely-found brand-specific assumption, if one exists"). No such assumption was found.

**Decision**: Ship US4 as a documented audit with **zero code changes** to the hub, publisher, or
frontend subscription — satisfies acceptance scenario 2 directly. `contracts/farm-hub.md` from
`specs/002-printer-integration/` remains the authoritative, unchanged contract; this feature does not
duplicate or supersede it.

## 7. Existing frontend verify-before-save pattern (context for §1/§2)

`EditPrinterDialog.tsx` (current, connection-only) already tracks a local `verified` boolean, reset
on every `ipAddress`/`accessCode` blur, and disables Save until a `verifyMutation` succeeds. The
broadened dialog keeps this exact mechanism but scopes it to only fire/gate when the operator has
actually touched connection fields: `connectionChanged = ipAddress !== currentIpAddress ||
accessCode.length > 0`. When `connectionChanged` is false, Save is gated only on the name being
non-empty (standard zod validation, no verify call, no `ipAddress`/`accessCode` sent in the request
body per §1's optional-pair shape). When true, the existing verify-then-enable-Save flow applies
unchanged.

## 8. Permission and activity-log reuse

**Decision**: The new `EditPrinterEndpoint` keeps the `[Permission("printers:edit-connection")]`
attribute string as-is (spec Assumptions explicitly rules out introducing a new permission for the
broadened edit). `GetPrinterCatalogEndpoint` reuses `[Permission("printers:register")]` per the same
Assumptions section. Both endpoints implement `IActivityLoggable` so the existing global
`ActivityLogPostProcessor` continues to log every successful mutation with zero per-endpoint logging
code — the edit's `EventType` is renamed from `"PrinterConnectionEdited"` to `"PrinterEdited"` to
reflect its broadened scope (a cosmetic rename of the logged event type, not a new logging
mechanism); the catalog endpoint is a `GET` and never implements `IActivityLoggable` (nothing
mutates).

Note for future work, not in scope here: `PermissionAuthorizationHandler` today only checks
`IsAuthenticated`, not the actual permission string — every `[Permission("...")]` attribute in the
codebase is currently decorative. This is a pre-existing gap unrelated to this feature; nothing in
005 depends on the permission string actually being enforced, and fixing that enforcement gap is out
of scope.
