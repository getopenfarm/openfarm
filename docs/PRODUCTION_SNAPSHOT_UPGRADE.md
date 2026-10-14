# Production snapshot upgrade

Finite jobs retain their source file and production inputs when queued. Library replacement,
reprocessing, units-per-print changes and deletion do not change those jobs or their retries.
Unlimited runs follow the current library part until each job is dispatched. Revision behavior
is fixed by quantity; the old revision-mode endpoint accepts only the corresponding fixed value.
Printer configuration, loaded inventory and production profiles continue to be checked live.

Before deploying, back up both PostgreSQL and the part-files volume. Deploy the API and web
changes together and restart the API. Startup applies the additive migration and the dispatcher
verifies legacy snapshots before processing assignments. Source files are retained after catalog
removal; this feature does not introduce file garbage collection.

Check the startup reconciliation summary and unresolved job IDs. Recovery accepts unchanged part
metadata only when its revision and stored file hash match the original job. Unsliced legacy inputs
also require an explicitly recorded slicer engine/version. Missing or ambiguous inputs remain
blocked and appear with a recovery reason in job details/matching diagnostics. Unfinished jobs
without snapshots protect their library part from deletion. The system does not substitute the latest revision.

Restore verified original files/metadata and restart the API to repeat reconciliation. If exact
historical inputs cannot be recovered, cancel the affected demand and create a replacement run
from the intended current part. Cancelled, Failed and Completed legacy jobs no longer block part
deletion. Deletion preserves their history, detaches their library reference and permanently marks
missing snapshots Unavailable with a removal timestamp. These jobs cannot retry or download files;
reconciliation skips them even after a restart. Cancellation without deletion remains recoverable.
Jobs with valid snapshots retain their existing retry and download behavior.

Active and Complete unlimited runs protect their source parts. Cancel or close them to release
the dependency once retained jobs either have snapshots or have finished. Unfinished jobs without
snapshots still protect the source until their cancellation is confirmed. Cancelling a single job
from an unlimited run does not cancel the run. Dispatched jobs with snapshots keep their inputs and
continue recording outcomes. Assembly dependencies still block part removal.

The migration preserves IDs, names, job revisions and existing history. Rolling back after deleting
catalog sources would violate the old required foreign key: restore the pre-upgrade database and
file backup together instead of downgrading a database containing detached historical references.
