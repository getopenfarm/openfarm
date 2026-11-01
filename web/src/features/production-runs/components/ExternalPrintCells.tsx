import { Link } from 'react-router';
import { Badge } from '@/components/ui/badge';
import type { QueueJobRowResponse } from '@/lib/api/generated/endpoints.schemas';
import { formatDateTime } from '@/lib/formatDateTime';
import { QueueTableCell } from './QueueTableSection';

/** Cells for a print the printer started itself: no units, queue slot or actions. */
export function ExternalPrintCells({
  job,
  printerName,
}: {
  job: QueueJobRowResponse;
  printerName?: string;
}) {
  const printerId = job.dispatchedPrinterId;
  return (
    <>
      <QueueTableCell className="flex items-center whitespace-normal">
        <Link to={`/jobs/${job.jobId}`} className="font-medium hover:underline">
          {job.partName}
        </Link>
      </QueueTableCell>
      <QueueTableCell className="flex items-center text-muted-foreground">
        —
      </QueueTableCell>
      <QueueTableCell className="flex items-center whitespace-normal">
        {printerId ? (
          <Link className="hover:underline" to={`/printers/${printerId}`}>
            {printerName ?? 'Unknown printer'}
          </Link>
        ) : (
          <span className="text-muted-foreground">Printer removed</span>
        )}
      </QueueTableCell>
      <QueueTableCell className="flex flex-col items-start justify-center gap-0.5 text-xs text-muted-foreground">
        <Badge
          variant={
            job.status === 'Failed'
              ? 'destructive'
              : job.status === 'Printing'
                ? 'default'
                : 'secondary'
          }
        >
          {job.status}
        </Badge>
        {job.startedAtUtc && (
          <span>Started {formatDateTime(job.startedAtUtc)}</span>
        )}
        {job.endedAtUtc && <span>Ended {formatDateTime(job.endedAtUtc)}</span>}
      </QueueTableCell>
      <QueueTableCell />
    </>
  );
}
