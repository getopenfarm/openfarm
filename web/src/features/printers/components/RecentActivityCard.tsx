import { BADGE_KIND_META } from '@/components/status-badge/kinds';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { ActivityDetails } from '@/features/activity-log/components/ActivityDetails';
import {
  activityEventKind,
  describeActivityEvent,
} from '@/features/activity-log/lib/activityEvent';
import { useOpenFarmApiEndpointsActivityLogGetActivityLogEndpoint } from '@/lib/api/generated/activity-log/activity-log';
import type {
  ActivityLogEntryResponse,
  ActivityLogPageResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { cn } from '@/lib/utils';

export interface RecentActivityCardProps {
  printerId: string;
}

function relativeTime(iso: string | undefined): string {
  if (!iso) return '';
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return '';
  const diffMs = Date.now() - then;
  const mins = Math.round(diffMs / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.round(hours / 24);
  return `${days}d ago`;
}

export function RecentActivityCard({ printerId }: RecentActivityCardProps) {
  const activityQuery =
    useOpenFarmApiEndpointsActivityLogGetActivityLogEndpoint({
      page: 1,
      pageSize: 20,
      entityType: 'Printer',
      entityId: printerId,
    });

  const page = activityQuery.data?.data as ActivityLogPageResponse | undefined;
  const entries = (page?.items ?? []).filter(
    (entry: ActivityLogEntryResponse) =>
      entry.entityType === 'Printer' && entry.entityId === printerId,
  );

  return (
    <Card className="gap-0 py-0">
      <CardHeader className="border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">Recent activity</CardTitle>
      </CardHeader>
      <CardContent className="p-0">
        {entries.length === 0 ? (
          <p className="px-[18px] py-7 text-center text-sm text-muted-foreground">
            No recent activity
          </p>
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
                    {entry.actorDisplayName
                      ? ` · ${entry.actorDisplayName}`
                      : ''}
                  </p>
                  <ActivityDetails details={entry.details} />
                  <p className="mt-0.5 text-[11.5px] text-muted-foreground">
                    {relativeTime(entry.timestampUtc)}
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
