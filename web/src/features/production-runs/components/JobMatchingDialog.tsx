import {
  CheckCircle2Icon,
  CircleMinusIcon,
  RefreshCwIcon,
  XCircleIcon,
} from 'lucide-react';
import { type ReactNode, useState } from 'react';
import { Link } from 'react-router';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import type { MatchingCheck } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPrintJobsGetJobMatchingDiagnosticsEndpoint } from '@/lib/api/generated/print-jobs/print-jobs';

function CheckRow({
  check,
  onNavigate,
}: {
  check: MatchingCheck;
  onNavigate: () => void;
}) {
  const Icon =
    check.status === 'Passed'
      ? CheckCircle2Icon
      : check.status === 'Failed'
        ? XCircleIcon
        : CircleMinusIcon;
  return (
    <li className="flex items-start gap-2 border-t py-3 first:border-t-0">
      <Icon
        aria-hidden
        className={`mt-0.5 size-4 shrink-0 ${check.status === 'Failed' ? 'text-destructive' : check.status === 'Passed' ? 'text-green-600' : 'text-muted-foreground'}`}
      />
      <div className="min-w-0 flex-1 text-sm">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="font-medium">{check.label}</span>
          <span className="text-xs text-muted-foreground">
            {check.status === 'NotEvaluated' ? 'Not evaluated' : check.status}
          </span>
        </div>
        {check.required && (
          <p className="text-muted-foreground">Required: {check.required}</p>
        )}
        {check.actual && <p>Actual: {check.actual}</p>}
        {check.message && (
          <p className="mt-1 break-words">
            {check.message.replace(
              /\s*Configure (?:in|profiles at) \/(?:filament|printers)\/[a-fA-F0-9-]+\/edit\.?/g,
              '',
            )}
          </p>
        )}
        {check.configurationUrl && check.status === 'Failed' && (
          <Link
            className="mt-1 inline-block underline underline-offset-2"
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

export function JobMatchingDialog({
  jobId,
  trigger,
}: {
  jobId: string;
  trigger?: ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const query =
    useOpenFarmApiEndpointsPrintJobsGetJobMatchingDiagnosticsEndpoint(jobId, {
      query: { enabled: open, staleTime: 0, refetchOnMount: 'always' },
    });
  const diagnostics = query.data?.status === 200 ? query.data.data : undefined;
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        {trigger ?? (
          <Button
            type="button"
            variant="link"
            size="sm"
            className="h-auto whitespace-normal px-0 text-xs"
          >
            Job diagnosis
          </Button>
        )}
      </DialogTrigger>
      <DialogContent className="max-h-[85vh] overflow-y-auto sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>Job diagnosis</DialogTitle>
          <DialogDescription>
            How this job’s requirements compare with each printer’s current
            configuration.
          </DialogDescription>
        </DialogHeader>
        <div className="flex items-center justify-between gap-3">
          <p className="text-xs text-muted-foreground">
            {diagnostics?.evaluatedAtUtc
              ? `Checked ${new Date(diagnostics.evaluatedAtUtc).toLocaleString()}`
              : 'Current matching checks'}
          </p>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={query.isFetching}
            onClick={() => query.refetch()}
          >
            <RefreshCwIcon className="size-3.5" />
            Refresh
          </Button>
        </div>
        {query.isLoading && <p role="status">Checking printers…</p>}
        {query.isError && (
          <p role="alert" className="text-sm text-destructive">
            Could not load matching diagnostics. Refresh to try again.
          </p>
        )}
        {diagnostics && (
          <div className="flex flex-col gap-4">
            <div className="rounded-lg border bg-muted/30 p-4">
              <div className="mb-2 flex items-center gap-2">
                <Badge
                  variant={
                    diagnostics.outcome === 'Eligible'
                      ? 'secondary'
                      : 'destructive'
                  }
                >
                  {diagnostics.outcome}
                </Badge>
                <span className="text-xs text-muted-foreground">
                  {diagnostics.requirementSource}
                </span>
              </div>
              <p className="break-words text-sm">{diagnostics.summary}</p>
            </div>
            <section className="rounded-lg border p-4">
              <h3 className="mb-2 text-sm font-semibold">
                Effective filament requirements
              </h3>
              <ul className="space-y-2">
                {(diagnostics.requirements ?? []).map((requirement) => (
                  <li
                    key={`${requirement.selectedVariantId ?? requirement.materialType}-${requirement.detectedColorHex}-${requirement.profileConstraintId}`}
                    className="text-sm"
                  >
                    <p>
                      {requirement.materialType ?? 'Any material'} ·{' '}
                      {requirement.selectedVariantId
                        ? (requirement.selectedVariantName ??
                          'Selected variant unavailable')
                        : (requirement.detectedColorHex ?? 'Any colour')}
                    </p>
                    {requirement.selectedVariantId && (
                      <p className="text-xs text-muted-foreground">
                        Catalog colour{' '}
                        {requirement.selectedColorHex ?? 'unavailable'} replaces
                        file colour{' '}
                        {requirement.detectedColorHex ?? 'unspecified'}.
                      </p>
                    )}
                    {requirement.profileConstraintId && (
                      <p className="text-xs text-muted-foreground">
                        A direct filament-profile constraint also applies.
                      </p>
                    )}
                  </li>
                ))}
              </ul>
            </section>
            <ul>
              {(diagnostics.jobChecks ?? []).map((check) => (
                <CheckRow
                  key={check.code}
                  check={check}
                  onNavigate={() => setOpen(false)}
                />
              ))}
            </ul>
            {(diagnostics.printers ?? []).length === 0 && (
              <p className="text-sm text-muted-foreground">
                No printers to evaluate.
              </p>
            )}
            {(diagnostics.printers ?? []).map((printer) => (
              <details
                key={printer.printerId}
                className="rounded-lg border p-4"
                open={diagnostics.printers?.length === 1 || undefined}
              >
                <summary className="cursor-pointer text-sm font-semibold">
                  {printer.printerName}
                  <span className="ml-2 font-normal text-muted-foreground">
                    {printer.eligible
                      ? 'Eligible'
                      : `${printer.checks?.filter((c) => c.status === 'Failed').length ?? 0} failed checks`}
                  </span>
                </summary>
                <ul className="mt-2">
                  {(printer.checks ?? []).map((check) => (
                    <CheckRow
                      key={`${check.code}-${check.required}`}
                      check={check}
                      onNavigate={() => setOpen(false)}
                    />
                  ))}
                </ul>
                {printer.schedulingNote && (
                  <p className="rounded-md bg-muted p-3 text-sm">
                    {printer.schedulingNote}
                  </p>
                )}
              </details>
            ))}
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}
