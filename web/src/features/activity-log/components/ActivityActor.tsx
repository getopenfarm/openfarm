import { Badge } from '@/components/ui/badge';
import type { ActivityLogEntryResponse } from '@/lib/api/generated/endpoints.schemas';

/** Actor name, plus the token or app it acted through (akaun's `actorLine`). */
export function ActivityActor({
  entry,
  fallback = 'System',
}: {
  entry: ActivityLogEntryResponse;
  fallback?: string;
}) {
  const name = entry.actorDisplayName ?? fallback;
  const via = entry.details?.via;
  if (!via?.label) return name;
  const title =
    via.kind === 'App'
      ? `${name} via ${via.label} (app)`
      : `${name} via token “${via.label}”`;
  return (
    <span title={title}>
      {name}{' '}
      <Badge kind="zinc" className="align-middle">
        {via.label}
      </Badge>
    </span>
  );
}
