import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import type { MatchedMaintenanceFlag } from '@/features/maintenance/lib/matchMaintenanceToFleet';
import { cn } from '@/lib/utils';

interface MaintenanceDuePanelProps {
  flags: MatchedMaintenanceFlag[];
  isLoading: boolean;
}

// Mock flags cross-referenced onto the real fleet via matchMaintenanceToFleet
// (see @/features/maintenance/lib/matchMaintenanceToFleet) — printer names are
// real, the flags themselves are fabricated until a Maintenance backend exists.
export function MaintenanceDuePanel({
  flags,
  isLoading,
}: MaintenanceDuePanelProps) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Maintenance due</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {isLoading ? (
          Array.from({ length: 3 }).map((_, index) => (
            // biome-ignore lint/suspicious/noArrayIndexKey: static skeleton placeholder rows
            <Skeleton key={`skeleton-${index}`} className="h-10 w-full" />
          ))
        ) : flags.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            No maintenance flags.
          </p>
        ) : (
          flags.map((flag) => {
            const isOverdue = flag.severity === 'overdue';
            return (
              <div
                key={`${flag.printerId}-${flag.task}`}
                className="flex items-start gap-2.5"
              >
                <span
                  className={cn(
                    'mt-1.5 size-1.5 shrink-0 rounded-full',
                    isOverdue ? 'bg-kind-red' : 'bg-kind-amber',
                  )}
                />
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-medium">
                    {flag.printerName}
                    <span className="ml-1.5 font-normal text-muted-foreground">
                      {flag.task}
                    </span>
                  </p>
                  <p
                    className={cn(
                      'truncate text-xs',
                      isOverdue ? 'text-kind-red' : 'text-kind-amber',
                    )}
                  >
                    {flag.detailText}
                  </p>
                </div>
              </div>
            );
          })
        )}
      </CardContent>
    </Card>
  );
}
