import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { BADGE_KIND_META } from '@/components/status-badge/kinds';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { ActivityActor } from '@/features/activity-log/components/ActivityActor';
import { ActivityDetails } from '@/features/activity-log/components/ActivityDetails';
import {
  activityEventKind,
  describeActivityEvent,
} from '@/features/activity-log/lib/activityEvent';
import type { PrinterActivityPageResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsActivityLogGetPrinterActivityEndpoint } from '@/lib/api/generated/printers/printers';
import { formatRelativeTime } from '@/lib/formatRelativeTime';
import { cn } from '@/lib/utils';

export interface RecentActivityCardProps {
  printerId: string;
}

export function RecentActivityCard({ printerId }: RecentActivityCardProps) {
  const activityQuery =
    useOpenFarmApiEndpointsActivityLogGetPrinterActivityEndpoint(printerId, {
      page: 1,
      pageSize: 20,
    });

  const page = activityQuery.data?.data as
    | PrinterActivityPageResponse
    | undefined;
  const entries = page?.items ?? [];

  return (
    <Card className="gap-0 py-0">
      <CardHeader className="border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">Recent activity</CardTitle>
      </CardHeader>
      <CardContent className="p-0">
        {activityQuery.isLoading ? (
          <Skeleton className="m-4 h-12" />
        ) : activityQuery.isError ? (
          <ErrorState
            bordered={false}
            description="Could not load printer activity."
            onRetry={() => activityQuery.refetch()}
          />
        ) : entries.length === 0 ? (
          <EmptyState size="sm" bordered={false} title="No recent activity" />
        ) : (
          <ul>
            {entries.map((entry) => (
              <li
                key={entry.id}
                className="flex gap-3 border-b px-[18px] py-3 last:border-b-0"
              >
                <span
                  className={cn(
                    'mt-1.5 size-[7px] shrink-0 rounded-full',
                    BADGE_KIND_META[
                      activityEventKind(entry.eventType ?? '', entry.details)
                    ].dotClass,
                  )}
                />
                <div className="min-w-0 flex-1">
                  <p className="text-[13px] text-foreground">
                    {describeActivityEvent(
                      entry.entityType ?? 'Printer',
                      entry.eventType ?? 'Activity',
                      entry.details,
                    )}
                    {entry.actorDisplayName && (
                      <>
                        {' · '}
                        <ActivityActor entry={entry} />
                      </>
                    )}
                  </p>
                  <ActivityDetails
                    details={entry.details}
                    entityType={entry.entityType}
                  />
                  <p className="mt-0.5 text-[11.5px] text-muted-foreground">
                    {formatRelativeTime(entry.timestampUtc)}
                  </p>
                </div>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
