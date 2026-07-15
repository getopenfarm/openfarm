import { Link } from 'react-router';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Progress } from '@/components/ui/progress';
import { Skeleton } from '@/components/ui/skeleton';
import type { FleetPrinterResponse } from '@/lib/api/generated/endpoints.schemas';

const MAX_ROWS = 6;

interface CurrentlyPrintingPanelProps {
  printers: FleetPrinterResponse[];
  isLoading: boolean;
}

export function CurrentlyPrintingPanel({
  printers,
  isLoading,
}: CurrentlyPrintingPanelProps) {
  const printing = printers
    .filter((printer) => printer.status === 'Printing')
    .slice(0, MAX_ROWS);

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between gap-2 space-y-0">
        <CardTitle>Currently printing</CardTitle>
        <Link
          to="/printers"
          className="text-xs font-medium text-muted-foreground hover:text-foreground hover:underline"
        >
          View fleet
        </Link>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {isLoading ? (
          Array.from({ length: 3 }).map((_, index) => (
            // biome-ignore lint/suspicious/noArrayIndexKey: static skeleton placeholder rows
            <Skeleton key={`skeleton-${index}`} className="h-12 w-full" />
          ))
        ) : printing.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            No printers currently printing.
          </p>
        ) : (
          printing.map((printer) => {
            const percent = printer.printProgressPercent ?? 0;
            return (
              <div
                key={printer.id ?? printer.name}
                className="flex flex-col gap-1.5"
              >
                <div className="flex items-center justify-between gap-2">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium">
                      {printer.name ?? 'Unnamed printer'}
                    </p>
                    <p className="truncate text-xs text-muted-foreground">
                      {printer.modelCode ?? printer.brand ?? '—'}
                    </p>
                  </div>
                  <span className="shrink-0 font-mono text-sm tabular-nums">
                    {percent}%
                  </span>
                </div>
                <Progress
                  value={percent}
                  indicatorClassName="bg-status-printing"
                />
              </div>
            );
          })
        )}
      </CardContent>
    </Card>
  );
}
