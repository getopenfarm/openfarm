# Part creation without printer setup

Approved October 5, 2026. This feature supersedes Spec 007's mandatory nozzle/material/estimate ingestion rules (FR-006 and related clarifications). A valid upload can be reviewed and confirmed with no registered printers, no linked profiles, or incomplete embedded metadata.

- File validity, print requirements, and estimation are independent. Structurally unreadable files still fail. Missing metadata and failed/skipped estimates keep previews reviewable and parts intact.
- Nozzle, print time, and weight are nullable. Time and weight are retained independently; `EstimateStatus` is `Resolving`, `Available`, `Partial`, or `Unavailable`. Missing values never become invented zeros. Multi-plate totals are known only when every plate supplies that metric.
- `PrintRequirementsStatus` is `Complete` only with a known positive nozzle and complete material requirements. Unsliced geometry can use the existing any-material requirement; unknown sliced-file materials cannot become wildcards.
- A 3MF is sliced when actual supported embedded plate G-code is present. Valid geometry-only 3MF remains importable without vendor project settings or slice metadata.
- No synthetic printer/profile is created. Existing profile and bundled-slicer selection rules supply optional estimates. Missing configuration is exposed through `processingWarnings` and `estimateUnavailableReason`.
- `POST /api/v1/parts/{id}/reprocess` starts a new `Reprocess` job using stored bytes. Review/confirm/discard reuse the processing-job API. Confirmation refreshes derived data while preserving identity, tags, file revision, upload time, and units per print. Compatible narrowing selections survive; incompatible selections are disclosed before clearing. Duplicate active retries and stale confirmations return 409; late estimates cannot overwrite newer confirmed processing.
- Incomplete parts remain valid library/BOM definitions but cannot enter a production run or dispatch. Missing estimates alone do not block production.
- Retry is explicit. Users replace sliced files whose original metadata is missing; manual metadata editing and automatic retry are outside this feature.

Validation covers no-printer/unlinked-printer uploads for every supported format, partial extraction and multi-plate totals, corrected replacement, retry/duplicate/stale/discard behavior, revision preservation, restart reconciliation, and production eligibility. Existing values are preserved by an additive EF migration; historical fabricated zero values are only corrected by explicit reprocessing.
