# Part management review

## Implemented in this change

- Removal is available from each library row and the detail page, with named confirmation,
  pending protection, actionable dependency errors, and retry.
- Parts referenced by assemblies or production runs (including completed runs) cannot be
  removed. Production history and dependent definitions remain intact.
- Catalog removal retains uploaded bytes: content-addressed paths can be shared by parts,
  processing jobs, and pinned job revisions. Reference-aware garbage collection is deferred.
- Library pagination uses the supported 25-row API page size, supports navigation beyond
  the first page, resets on tag changes, and recovers after removing the final row on a page.
  Name sorting applies to the displayed page only.
- Library and detail request failures have Retry states distinct from missing/empty results.

## Remaining gaps

| Capability | Current state | Follow-up |
| --- | --- | --- |
| Rename an existing part | Name is set at creation; no rename endpoint or control | Add a validated metadata update endpoint and routed edit page |
| Edit existing part tags | Tag/untag endpoints exist; detail tags are read-only | Expose tag editing, ideally on the metadata edit page |
| Download original file | Authenticated file endpoint exists; no visible action | Add an authenticated download action using the original filename |
| Search and catalog-wide sorting | Tag filtering and page-local name sorting only | Add server query/sort support with matching controls |
| Archive and restore | Permanent deletion only; referenced parts are protected | Define archive semantics, production eligibility, visibility, and restore behavior |
| Bulk management | Single-part actions only | Define selection, partial failures, and confirmation behavior |
| File reclamation | Shared bytes intentionally retained | Build reference-aware cleanup covering parts, processing jobs, and pinned revisions |

Creation, file replacement, reprocessing, filament narrowing, units-per-print editing,
and queueing already have UI and backend implementations. Their presence is not a claim
that every workflow was exhaustively audited in this removal change.
