import {
  CheckCircle2Icon,
  CircleMinusIcon,
  InfoIcon,
  RefreshCwIcon,
  XCircleIcon,
} from 'lucide-react';
import { type ReactNode, useState } from 'react';
import { Link } from 'react-router';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import { stripConfigurationLink } from '@/features/slicing/stripConfigurationLink';
import type {
  MatchingCheck,
  MatchingFilamentRequirement,
  PrinterMatchingDiagnostics,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPrintJobsGetJobMatchingDiagnosticsEndpoint } from '@/lib/api/generated/print-jobs/print-jobs';
import { formatDateTime } from '@/lib/formatDateTime';
import { cn } from '@/lib/utils';

const failed = (checks: MatchingCheck[] | null | undefined) =>
  (checks ?? []).filter((c) => c.status === 'Failed');

/** "Printing", "0.6 mm", or the check name when the value is too long for the list. */
function shortReason(printer: PrinterMatchingDiagnostics) {
  if (printer.eligible) return 'Ready';
  const [first, ...rest] = failed(printer.checks);
  if (!first) return 'Not ready';
  const text =
    first.actual && first.actual.length <= 18 ? first.actual : first.label;
  return rest.length ? `${text} +${rest.length}` : text;
}

function CheckIcon({ status }: { status?: string }) {
  const Icon =
    status === 'Passed'
      ? CheckCircle2Icon
      : status === 'Failed'
        ? XCircleIcon
        : CircleMinusIcon;
  return (
    <Icon
      aria-hidden
      className={cn(
        'mt-0.5 size-4 shrink-0',
        status === 'Failed'
          ? 'text-destructive'
          : status === 'Passed'
            ? 'text-status-complete'
            : 'text-muted-foreground',
      )}
    />
  );
}

function CheckRow({
  check,
  onNavigate,
}: {
  check: MatchingCheck;
  onNavigate: () => void;
}) {
  const isFailed = check.status === 'Failed';
  return (
    <li className="flex items-start gap-2 py-2">
      <CheckIcon status={check.status} />
      <div className="min-w-0 flex-1 text-sm">
        <div className="flex items-baseline justify-between gap-3">
          <p
            className={cn(
              isFailed ? 'font-medium' : 'text-muted-foreground',
              check.status === 'Passed' && 'text-foreground',
            )}
          >
            {check.label}
          </p>
          {!isFailed && (
            <span className="shrink-0 truncate text-xs text-muted-foreground">
              {check.status === 'Passed' ? check.actual : 'Skipped'}
            </span>
          )}
        </div>
        {isFailed && (check.required || check.actual) && (
          <dl className="mt-1 grid grid-cols-[auto_1fr] gap-x-3 text-xs">
            {check.required && (
              <>
                <dt className="text-muted-foreground">Needs</dt>
                <dd className="break-words">{check.required}</dd>
              </>
            )}
            {check.actual && (
              <>
                <dt className="text-muted-foreground">Has</dt>
                <dd className="break-words">{check.actual}</dd>
              </>
            )}
          </dl>
        )}
        {check.message && (
          <p className="mt-1 break-words text-xs text-muted-foreground">
            {stripConfigurationLink(check.message)}
          </p>
        )}
        {isFailed && check.configurationUrl && (
          <Link
            className="mt-1 inline-block text-xs underline underline-offset-2"
            to={check.configurationUrl}
            onClick={onNavigate}
          >
            Review configuration
          </Link>
        )}
      </div>
    </li>
  );
}

const STATUS_ORDER: Record<string, number> = { Failed: 0, Passed: 1 };

/** Every check in one fixed order: failed, then passed, then skipped. */
function PrinterDetail({
  printer,
  onNavigate,
}: {
  printer: PrinterMatchingDiagnostics;
  onNavigate: () => void;
}) {
  const checks = [...(printer.checks ?? [])].sort(
    (a, b) =>
      (STATUS_ORDER[a.status ?? ''] ?? 2) - (STATUS_ORDER[b.status ?? ''] ?? 2),
  );
  return (
    <div className="min-w-0 flex-1 px-4 py-2 sm:overflow-y-auto">
      {printer.schedulingNote && (
        <p className="my-2 flex items-start gap-2 rounded-md bg-muted px-3 py-2 text-xs">
          <InfoIcon aria-hidden className="mt-0.5 size-3.5 shrink-0" />
          {printer.schedulingNote}
        </p>
      )}
      <ul className="divide-y">
        {checks.map((check) => (
          <CheckRow
            key={`${check.code}-${check.required}`}
            check={check}
            onNavigate={onNavigate}
          />
        ))}
      </ul>
      <Link
        to={`/printers/${printer.printerId}`}
        onClick={onNavigate}
        className="my-2 inline-block text-xs underline underline-offset-2"
      >
        Open printer
      </Link>
    </div>
  );
}

function RequirementChip({
  requirement,
}: {
  requirement: MatchingFilamentRequirement;
}) {
  const color = requirement.selectedColorHex ?? requirement.detectedColorHex;
  const name = requirement.selectedVariantId
    ? (requirement.selectedVariantName ?? 'Variant unavailable')
    : [requirement.materialType ?? 'Any material', color ?? 'any color'].join(
        ' · ',
      );
  const notes = [
    requirement.selectedVariantId &&
      `Catalog color ${requirement.selectedColorHex ?? 'unavailable'} replaces file color ${requirement.detectedColorHex ?? 'unspecified'}.`,
    requirement.profileConstraintId && 'Filament profile is also fixed.',
  ].filter(Boolean);
  return (
    <li
      className="inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs"
      title={notes.join(' ') || undefined}
    >
      <span
        aria-hidden
        className="size-3 rounded-full border"
        style={{ backgroundColor: color ?? 'transparent' }}
      />
      {name}
    </li>
  );
}

export function JobMatchingDialog({
  jobId,
  trigger,
}: {
  jobId: string;
  trigger?: ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const [selectedId, setSelectedId] = useState<string>();
  const query =
    useOpenFarmApiEndpointsPrintJobsGetJobMatchingDiagnosticsEndpoint(jobId, {
      query: { enabled: open, staleTime: 0, refetchOnMount: 'always' },
    });
  const diagnostics = query.data?.status === 200 ? query.data.data : undefined;
  const close = () => setOpen(false);

  const printers = [...(diagnostics?.printers ?? [])].sort(
    (a, b) =>
      Number(b.eligible) - Number(a.eligible) ||
      failed(a.checks).length - failed(b.checks).length,
  );
  const pinned = printers.find((p) =>
    p.checks?.some(
      (c) =>
        c.code === 'pin' &&
        c.status === 'Passed' &&
        c.required?.startsWith('Pinned'),
    ),
  );
  const selected =
    printers.find((p) => p.printerId === selectedId) ?? pinned ?? printers[0];
  const matchCount = printers.filter((p) => p.eligible).length;
  const jobFailures = failed(diagnostics?.jobChecks);

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) setSelectedId(undefined);
      }}
    >
      <DialogTrigger asChild>
        {trigger ?? (
          <Button
            type="button"
            variant="link"
            size="sm"
            className="h-auto whitespace-normal px-0 text-xs"
          >
            Printer matching
          </Button>
        )}
      </DialogTrigger>
      <DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-3xl">
        <DialogHeader>
          <div className="flex items-center gap-2 pr-8">
            <DialogTitle>Printer matching</DialogTitle>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="size-7"
              aria-label="Refresh"
              disabled={query.isFetching}
              onClick={() => query.refetch()}
            >
              <RefreshCwIcon
                className={cn('size-4', query.isFetching && 'animate-spin')}
              />
            </Button>
          </div>
          <DialogDescription className="sr-only">
            Matching checks per printer
          </DialogDescription>
        </DialogHeader>

        {query.isLoading && (
          <div
            role="status"
            aria-label="Checking printers"
            className="grid gap-2"
          >
            <Skeleton className="h-6 w-1/2" />
            <Skeleton className="h-40 w-full" />
          </div>
        )}
        {query.isError && (
          <p role="alert" className="text-sm text-destructive">
            Could not load printer matching. Refresh to retry.
          </p>
        )}

        {diagnostics && (
          <div className="flex min-w-0 flex-col gap-4">
            <div className="flex flex-col gap-2">
              <p className="font-medium">
                {printers.length
                  ? `${matchCount}/${printers.length} printers match`
                  : jobFailures.length
                    ? 'Job not ready'
                    : 'No printers'}
              </p>
              {(diagnostics.requirements ?? []).length > 0 && (
                <div className="flex flex-wrap items-center gap-2">
                  <span className="text-xs text-muted-foreground">Needs</span>
                  <ul className="flex flex-wrap gap-1.5">
                    {(diagnostics.requirements ?? []).map((requirement) => (
                      <RequirementChip
                        key={`${requirement.selectedVariantId ?? requirement.materialType}-${requirement.detectedColorHex}-${requirement.profileConstraintId}`}
                        requirement={requirement}
                      />
                    ))}
                  </ul>
                </div>
              )}
            </div>

            {jobFailures.length > 0 && (
              <ul className="divide-y rounded-lg border px-4">
                {jobFailures.map((check) => (
                  <CheckRow key={check.code} check={check} onNavigate={close} />
                ))}
              </ul>
            )}

            {selected && (
              <div className="flex flex-col overflow-hidden rounded-lg border sm:h-96 sm:flex-row">
                <ul
                  aria-label="Printers"
                  className="flex max-h-48 shrink-0 flex-col gap-1 overflow-y-auto border-b bg-muted/40 p-2 sm:max-h-none sm:w-60 sm:border-r sm:border-b-0"
                >
                  {printers.map((printer) => {
                    const isSelected = printer.printerId === selected.printerId;
                    return (
                      <li key={printer.printerId}>
                        <button
                          type="button"
                          aria-pressed={isSelected}
                          onClick={() => setSelectedId(printer.printerId)}
                          className={cn(
                            'flex w-full items-start gap-2.5 rounded-md px-3 py-2.5 text-left hover:bg-background focus-visible:outline-2 focus-visible:outline-ring',
                            isSelected &&
                              'bg-primary/10 ring-1 ring-primary/30 hover:bg-primary/10',
                          )}
                        >
                          <CheckIcon
                            status={printer.eligible ? 'Passed' : 'Failed'}
                          />
                          <span className="min-w-0 flex-1">
                            <span
                              className={cn(
                                'block truncate text-sm font-medium',
                                isSelected && 'text-primary',
                              )}
                            >
                              {printer.printerName}
                            </span>
                            <span className="block truncate text-xs text-muted-foreground">
                              {shortReason(printer)}
                            </span>
                          </span>
                        </button>
                      </li>
                    );
                  })}
                </ul>
                <PrinterDetail printer={selected} onNavigate={close} />
              </div>
            )}

            <p className="text-xs text-muted-foreground">
              Checked{' '}
              {diagnostics.evaluatedAtUtc
                ? formatDateTime(diagnostics.evaluatedAtUtc)
                : 'now'}
            </p>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
