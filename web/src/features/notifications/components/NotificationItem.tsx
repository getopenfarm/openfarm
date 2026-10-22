import { Link } from 'react-router';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import type {
  LowStockNotificationResponse,
  NotificationItemResponse,
  NotificationLineResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { formatRelativeTime } from '@/lib/formatRelativeTime';
import { formatGrams, formatWeight } from '@/lib/formatUnits';
import { canAssignSpool, lineLabel, reasonLabel } from '../lib/notifications';

interface NotificationItemProps {
  item: NotificationItemResponse;
  printerName?: string;
  dismissing: boolean;
  onAssign: (line: NotificationLineResponse) => void;
  onDismiss: () => void;
}

/** One print whose filament was not deducted: what it was, and one row per line that needs a spool. */
export function NotificationItem({
  item,
  printerName,
  dismissing,
  onAssign,
  onDismiss,
}: NotificationItemProps) {
  const meta = [
    printerName,
    item.status,
    formatRelativeTime(item.endedAtUtc),
  ].filter(Boolean);
  return (
    <li className="flex flex-col gap-2 px-3 py-3">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <Link
            to={`/jobs/${item.printId}`}
            className="block truncate text-sm font-medium hover:underline"
          >
            {item.name}
          </Link>
          <p className="text-xs text-muted-foreground">{meta.join(' · ')}</p>
        </div>
        <Button
          type="button"
          variant="ghost"
          size="xs"
          disabled={dismissing}
          aria-label={`Dismiss ${item.name}`}
          onClick={onDismiss}
        >
          Dismiss
        </Button>
      </div>
      <ul className="flex flex-col gap-2">
        {(item.lines ?? []).map((line) => (
          <li key={line.lineId} className="flex items-center gap-2">
            <Swatch colorHex={line.colorHex ?? undefined} size="md" />
            <div className="min-w-0 flex-1">
              <p className="truncate text-[13px]">
                {lineLabel(line)}
                {line.estimatedGrams != null && (
                  <span className="ml-2 text-muted-foreground tabular-nums">
                    {formatGrams(line.estimatedGrams)}
                  </span>
                )}
              </p>
              <p className="text-xs text-kind-amber">{reasonLabel(line)}</p>
            </div>
            {canAssignSpool(line) && (
              <Button
                type="button"
                variant="outline"
                size="xs"
                aria-label={`Assign spool to ${lineLabel(line)} of ${item.name}`}
                onClick={() => onAssign(line)}
              >
                Assign spool
              </Button>
            )}
          </li>
        ))}
      </ul>
    </li>
  );
}

/** A color under its low-stock threshold; it clears once restocked, so there is nothing to dismiss. */
export function LowStockNotification({
  item,
}: {
  item: LowStockNotificationResponse;
}) {
  return (
    <li className="flex items-center gap-2 px-3 py-3">
      <Swatch colorHex={item.colorHex ?? undefined} size="md" />
      <div className="min-w-0 flex-1">
        <Link
          to={`/filament/${item.filamentId}/edit`}
          className="block truncate text-sm font-medium hover:underline"
        >
          {item.vendorName} · {item.material} · {item.colorName}
        </Link>
        <p className="text-xs text-kind-amber tabular-nums">
          {formatWeight(item.onHandGrams ?? 0)} on hand · below{' '}
          {formatWeight(item.thresholdGrams ?? 0)}
        </p>
      </div>
    </li>
  );
}
