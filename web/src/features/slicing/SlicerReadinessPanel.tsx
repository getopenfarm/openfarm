import { AlertTriangleIcon, CheckCircle2Icon } from 'lucide-react';
import { Link } from 'react-router';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { useOpenFarmApiEndpointsSlicingGetSlicingReadinessEndpoint } from '@/lib/api/generated/slicing/slicing';
import { formatSlicer } from './formatSlicer';

interface SlicerReadinessPanelProps {
  /** The exact slicer a part is (or would be) pinned to. */
  engine: string;
  version: string;
  /** Only this printer's problems, and the filament assignments that apply to it. */
  printerId?: string;
  className?: string;
}

/**
 * What would not print with one exact slicer on this farm: a printer's machine or process preset, or a filament
 * assignment, that the slicer's own catalog has no copy of. Production never substitutes another slicer's preset,
 * so each line here is a job that would be blocked — with a link to the page that fixes it.
 */
export function SlicerReadinessPanel({
  engine,
  version,
  printerId,
  className,
}: SlicerReadinessPanelProps) {
  const query = useOpenFarmApiEndpointsSlicingGetSlicingReadinessEndpoint(
    { engine, version },
    { query: { enabled: Boolean(engine && version) } },
  );
  const slicer = formatSlicer(engine, version);

  if (query.isLoading) {
    return <Skeleton className={className ?? 'h-9 w-full'} />;
  }

  const report = query.data?.data;
  if (query.isError || !report) {
    return (
      <p className="text-muted-foreground text-xs">Could not check {slicer}.</p>
    );
  }

  const rows = [
    ...(report.printers ?? [])
      .filter((printer) => !printerId || printer.printerId === printerId)
      .flatMap((printer) =>
        (printer.issues ?? []).map((issue) => ({
          who: printer.printerName ?? 'Printer',
          issue,
        })),
      ),
    ...(report.filaments ?? [])
      .filter(
        (filament) => !printerId || filament.printerIds?.includes(printerId),
      )
      .flatMap((filament) =>
        (filament.issues ?? []).map((issue) => ({
          who: `${filament.filamentName} · ${filament.modelCode} ${filament.nozzleDiameterMm} mm`,
          issue,
        })),
      ),
  ];

  if (rows.length === 0) {
    return (
      <p
        className={`flex items-center gap-1.5 text-muted-foreground text-xs ${className ?? ''}`}
      >
        <CheckCircle2Icon className="size-3.5" />
        {printerId
          ? 'This printer resolves'
          : 'Every printer and filament resolves'}{' '}
        in {slicer}.
      </p>
    );
  }

  return (
    <Alert
      variant="destructive"
      className={className}
      data-testid="slicer-readiness-warning"
    >
      <AlertTriangleIcon />
      <AlertTitle>Production is blocked with {slicer}</AlertTitle>
      <AlertDescription>
        <ul className="flex flex-col gap-1">
          {rows.map(({ who, issue }) => (
            <li key={`${who}:${issue.kind}:${issue.code}:${issue.message}`}>
              <span className="font-medium">{who}:</span> {issue.message}{' '}
              {issue.link && (
                <Link to={issue.link} className="whitespace-nowrap">
                  Fix
                </Link>
              )}
            </li>
          ))}
        </ul>
      </AlertDescription>
    </Alert>
  );
}
