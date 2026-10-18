import type {
  NotificationItemResponse,
  NotificationLineResponse,
} from '@/lib/api/generated/endpoints.schemas';

const REASON_LABEL: Record<string, string> = {
  NoSpool: 'No spool',
  SpoolNotLoaded: 'Spool not loaded',
  NoEstimate: 'No estimate',
  NoProgress: 'No progress',
};

export const reasonLabel = (line: NotificationLineResponse) =>
  REASON_LABEL[line.reason ?? ''] ?? 'Not deducted';

/** Only a missing or unloaded spool can be fixed by choosing one; the other reasons need the operator's own numbers. */
export const canAssignSpool = (line: NotificationLineResponse) =>
  line.reason === 'NoSpool' || line.reason === 'SpoolNotLoaded';

export const bellLabel = (count: number) =>
  count > 0 ? `Notifications, ${count} need action` : 'Notifications';

export const badgeText = (count: number) => (count > 9 ? '9+' : String(count));

/** "PLA #000000", or "Any material" when the file named none. */
export function lineLabel(line: NotificationLineResponse) {
  const color = line.colorHex ? `#${line.colorHex.replace(/^#/, '')}` : '';
  return [line.materialType || 'Any material', color].filter(Boolean).join(' ');
}

export const findLine = (
  items: NotificationItemResponse[],
  printId: string,
  lineId: string,
) => {
  const item = items.find((candidate) => candidate.printId === printId);
  const line = item?.lines?.find((candidate) => candidate.lineId === lineId);
  return item && line ? { item, line } : undefined;
};
