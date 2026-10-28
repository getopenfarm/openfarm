import { ChevronRightIcon } from 'lucide-react';
import { Link } from 'react-router';
import { Badge } from '@/components/ui/badge';
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type {
  PartRequirementSummaryResponse,
  RunJobSummaryResponse,
  RunLineSummaryResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { formatShortDateTime } from '@/lib/formatDateTime';
import { formatMinutes } from '@/lib/formatMinutes';
import { pluralize } from '@/lib/pluralize';
import { cn } from '@/lib/utils';
import { PlateStrip } from './PlateStrip';
import { TargetThumbnail } from './TargetPicker';

type PrinterName = (id?: string | null) => string | undefined;

const units = (n?: number | null) => Number((n ?? 0).toFixed(2));

function plateStatus(job: RunJobSummaryResponse) {
  if (job.writtenOff)
    return { label: 'Written off', className: 'text-muted-foreground' };
  if (job.requeuedAsJobId)
    return { label: 'Requeued', className: 'text-muted-foreground' };
  if (job.needsAction)
    return {
      label: job.status ?? '',
      className: 'font-medium text-destructive',
    };
  if (job.status === 'Printing' || job.status === 'Paused')
    return { label: job.status, className: 'font-medium text-status-printing' };
  if (job.status === 'Completed')
    return { label: 'Completed', className: 'text-status-complete' };
  return { label: job.status ?? '', className: 'text-muted-foreground' };
}

/** Start and duration, or how long it has been printing: shorter to scan than two full timestamps. */
function when(job: RunJobSummaryResponse) {
  if (job.startedAtUtc && job.endedAtUtc)
    return `${formatShortDateTime(job.startedAtUtc)} · ${formatMinutes(
      (Date.parse(job.endedAtUtc) - Date.parse(job.startedAtUtc)) / 60_000,
    )}`;
  if (job.startedAtUtc) return `Since ${formatShortDateTime(job.startedAtUtc)}`;
  if (job.endedAtUtc) return formatShortDateTime(job.endedAtUtc);
  return '—';
}

function Plates({
  jobs,
  printerName,
}: {
  jobs: RunJobSummaryResponse[];
  printerName: PrinterName;
}) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="w-24">Plate</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Printer</TableHead>
          <TableHead>Time</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {jobs.map((job, index) => {
          const status = plateStatus(job);
          return (
            <TableRow key={job.id}>
              <TableCell>
                <Link
                  to={`/jobs/${job.id}`}
                  className="tabular-nums hover:underline"
                >
                  Plate {index + 1}
                </Link>
              </TableCell>
              <TableCell className={status.className}>
                {status.label}
                {job.lastBlockReason && (
                  <span className="block text-xs font-normal text-status-attention">
                    Send failed
                  </span>
                )}
              </TableCell>
              <TableCell>
                {job.printerId ? (
                  <Link
                    to={`/printers/${job.printerId}`}
                    className="hover:underline"
                  >
                    {printerName(job.printerId) ?? 'Removed printer'}
                  </Link>
                ) : (
                  <span className="text-muted-foreground">
                    {job.status === 'Queued' ? 'Automatic' : '—'}
                  </span>
                )}
              </TableCell>
              <TableCell className="text-muted-foreground tabular-nums">
                {when(job)}
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}

function RequirementRow({
  requirement,
  unlimited,
  thumbnail,
  printerName,
  indent,
}: {
  requirement: PartRequirementSummaryResponse;
  unlimited: boolean;
  thumbnail?: string;
  printerName: PrinterName;
  indent?: boolean;
}) {
  const jobs = requirement.jobs ?? [];
  return (
    <Collapsible className="group/requirement">
      <CollapsibleTrigger
        disabled={jobs.length === 0}
        className={cn(
          'grid w-full grid-cols-[1.25rem_2.5rem_minmax(0,1fr)] items-center gap-3 rounded-md py-2 pr-2 text-left hover:bg-muted/50 disabled:hover:bg-transparent sm:grid-cols-[1.25rem_2.5rem_minmax(0,1fr)_minmax(0,1.5fr)_6.5rem]',
          indent && 'pl-6',
        )}
      >
        <ChevronRightIcon
          className={cn(
            'size-4 text-muted-foreground transition-transform group-data-[state=open]/requirement:rotate-90',
            jobs.length === 0 && 'invisible',
          )}
        />
        <TargetThumbnail kind="Part" thumbnailBase64={thumbnail} />
        <span className="min-w-0">
          <span className="block truncate font-medium">
            {requirement.partName}
          </span>
          <span className="block text-xs text-muted-foreground">
            {requirement.unitsPerPlate ?? 1} per plate
            {!requirement.sourcePartAvailable && ' · removed from library'}
          </span>
        </span>
        <span className="col-span-3 col-start-3 sm:col-span-1 sm:col-start-auto">
          {unlimited ? null : <PlateStrip plates={requirement.plates ?? {}} />}
        </span>
        <span className="col-span-3 col-start-3 text-sm tabular-nums sm:col-span-1 sm:col-start-auto sm:text-right">
          {unlimited
            ? `${requirement.completedUnits ?? 0} produced`
            : `${Math.min(units(requirement.completedUnits), units(requirement.quantity))}/${units(requirement.quantity)} units`}
        </span>
      </CollapsibleTrigger>
      <CollapsibleContent className="pb-2 pl-8">
        <Plates jobs={jobs} printerName={printerName} />
      </CollapsibleContent>
    </Collapsible>
  );
}

/** Each line of the run; a part line is one row, an assembly line lists its parts beneath it. */
export function RunLines({
  lines,
  thumbnailOf,
  printerName,
}: {
  lines: RunLineSummaryResponse[];
  /** A part's first plate thumbnail, when the part is still in the library. */
  thumbnailOf: (partId?: string) => string | undefined;
  printerName: PrinterName;
}) {
  return (
    <div className="flex flex-col divide-y">
      {lines.map((line) => {
        const unlimited = line.quantity == null;
        const requirements = line.requirements ?? [];
        const pinned = line.pinnedPrinterId
          ? printerName(line.pinnedPrinterId)
          : undefined;
        const meta = [
          unlimited ? 'Unlimited' : null,
          unlimited
            ? `${pluralize(line.backfillPrinterCap ?? 1, 'printer')} at once${line.backfillCumulativeUnitCeiling != null ? `, stops at ${line.backfillCumulativeUnitCeiling} units` : ''}`
            : null,
          pinned ? `Pinned to ${pinned}` : null,
        ].filter(Boolean);

        if (line.targetKind === 'Part' && requirements.length === 1) {
          return (
            <div key={line.id} className="py-1">
              <RequirementRow
                requirement={requirements[0]}
                unlimited={unlimited}
                thumbnail={thumbnailOf(requirements[0].partId)}
                printerName={printerName}
              />
              {(meta.length > 0 || line.spansMultipleRevisions) && (
                <p className="flex flex-wrap items-center gap-2 pb-2 pl-24 text-xs text-muted-foreground">
                  {meta.join(' · ')}
                  {line.spansMultipleRevisions && (
                    <Badge variant="outline">Spans several revisions</Badge>
                  )}
                </p>
              )}
            </div>
          );
        }

        return (
          <div key={line.id} className="py-2">
            <div className="flex items-center gap-3 pl-8">
              <TargetThumbnail kind="Assembly" />
              <div className="min-w-0">
                <span className="block truncate font-medium">
                  {line.targetName}
                  {!unlimited && (
                    <span className="font-normal text-muted-foreground tabular-nums">
                      {' '}
                      ×{line.quantity}
                    </span>
                  )}
                </span>
                <span className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                  {['Assembly', ...meta].join(' · ')}
                  {line.spansMultipleRevisions && (
                    <Badge variant="outline">Spans several revisions</Badge>
                  )}
                </span>
              </div>
            </div>
            {requirements.map((requirement) => (
              <RequirementRow
                key={requirement.id}
                requirement={requirement}
                unlimited={unlimited}
                thumbnail={thumbnailOf(requirement.partId)}
                printerName={printerName}
                indent
              />
            ))}
          </div>
        );
      })}
    </div>
  );
}
