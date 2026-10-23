import { Link } from 'react-router';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import { normalizeFilamentColor } from '@/features/printers/lib/feedSlotView';
import type {
  LowStockNotificationResponse,
  NotificationItemResponse,
  NotificationLineResponse,
  RfidWeightDriftResponse,
  UnregisteredRfidSpoolResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { formatRelativeTime } from '@/lib/formatRelativeTime';
import { formatDays, formatGrams, formatWeight } from '@/lib/formatUnits';
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

/** A color under its low-stock threshold, in grams or in days left; it clears once restocked, so there is nothing to dismiss. */
export function LowStockNotification({
  item,
}: {
  item: LowStockNotificationResponse;
}) {
  const onHand = item.onHandGrams ?? 0;
  const lowByGrams =
    item.thresholdGrams != null && onHand < item.thresholdGrams;
  const lowByDays =
    item.thresholdDays != null &&
    item.daysLeft != null &&
    item.daysLeft < item.thresholdDays;
  return (
    <li className="flex items-center gap-2 px-3 py-3">
      <Swatch colorHex={item.colorHex ?? undefined} size="md" />
      <div className="min-w-0 flex-1">
        <Link
          to={`/filament/${item.filamentId}`}
          className="block truncate text-sm font-medium hover:underline"
        >
          {item.vendorName} · {item.material} · {item.colorName}
        </Link>
        {lowByDays && (
          <p className="text-xs text-kind-amber tabular-nums">
            {formatDays(item.daysLeft ?? 0)} left · below {item.thresholdDays} d
          </p>
        )}
        {(lowByGrams || !lowByDays) && (
          <p className="text-xs text-kind-amber tabular-nums">
            {formatWeight(onHand)} on hand
            {lowByGrams && ` · below ${formatWeight(item.thresholdGrams ?? 0)}`}
          </p>
        )}
      </div>
    </li>
  );
}

/** The add-spool page, prefilled from what the printer reads: the tag, the likely color, and its weights. */
export function registerRfidSpoolUrl(item: UnregisteredRfidSpoolResponse) {
  const params = new URLSearchParams({
    rfidId: item.rfidId ?? '',
    slot: `${item.printerName} · ${item.slotLabel}`,
  });
  if (item.filamentVariantId) params.set('variantId', item.filamentVariantId);
  if (item.rfidWeightGrams)
    params.set('spoolWeightGrams', String(item.rfidWeightGrams));
  if (item.remainingGrams != null)
    params.set('remainingWeightGrams', String(item.remainingGrams));
  return `/filament/spools/new?${params}`;
}

/** A tagged spool a printer reads that no spool owns: register it, or link the tag to the untagged spool in that slot. */
export function UnregisteredRfidSpoolNotification({
  item,
  linking,
  onLink,
}: {
  item: UnregisteredRfidSpoolResponse;
  linking: boolean;
  onLink: () => void;
}) {
  const name =
    item.variantName ??
    item.subBrand ??
    item.materialType ??
    'Unknown filament';
  return (
    <li className="flex flex-col gap-2 px-3 py-3">
      <div className="min-w-0">
        <p className="text-sm font-medium">Unregistered RFID spool</p>
        <p className="text-xs text-muted-foreground">
          {item.printerName} · {item.slotLabel}
        </p>
      </div>
      <div className="flex items-center gap-2">
        <Swatch colorHex={normalizeFilamentColor(item.colorHex)} size="md" />
        <span className="min-w-0 flex-1 truncate text-sm">
          {name}
          {item.remainPercent != null && ` · ${item.remainPercent}%`}
        </span>
        <Button asChild type="button" variant="outline" size="xs">
          <Link to={registerRfidSpoolUrl(item)}>Register</Link>
        </Button>
      </div>
      {item.loadedSpoolId && (
        <div className="flex items-center gap-2">
          <span className="min-w-0 flex-1 truncate text-xs text-muted-foreground">
            Loaded without tag: {item.loadedSpoolName ?? 'spool'}
          </span>
          <Button
            type="button"
            variant="outline"
            size="xs"
            disabled={linking}
            onClick={onLink}
          >
            {linking ? 'Linking…' : 'Link'}
          </Button>
        </div>
      )}
    </li>
  );
}

/** A loaded tagged spool the AMS reads well off its recorded weight: one click sets it to the reading, as a correction. */
export function RfidWeightDriftNotification({
  item,
  correcting,
  onCorrect,
}: {
  item: RfidWeightDriftResponse;
  correcting: boolean;
  onCorrect: () => void;
}) {
  const read = formatWeight(item.readGrams ?? 0);
  return (
    <li className="flex flex-col gap-2 px-3 py-3">
      <div className="min-w-0">
        <Link
          to={`/filament/spools/${item.spoolId}`}
          className="block truncate text-sm font-medium hover:underline"
        >
          {item.spoolLabel ??
            ([item.material, item.colorName].filter(Boolean).join(' · ') ||
              'Spool')}
        </Link>
        <p className="text-xs text-muted-foreground">
          {item.printerName} · {item.slotLabel}
        </p>
      </div>
      <div className="flex items-center gap-2">
        <span className="min-w-0 flex-1 text-xs text-kind-amber tabular-nums">
          AMS reads ~{read} · inventory {formatWeight(item.inventoryGrams ?? 0)}
        </span>
        <Button
          type="button"
          variant="outline"
          size="xs"
          disabled={correcting}
          onClick={onCorrect}
        >
          {correcting ? 'Correcting…' : `Correct to ${read}`}
        </Button>
      </div>
    </li>
  );
}
