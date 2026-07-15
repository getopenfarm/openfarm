const RELATIVE_TIME_FORMAT = new Intl.RelativeTimeFormat(undefined, {
  numeric: 'auto',
});

const UNITS: Array<[Intl.RelativeTimeFormatUnit, number]> = [
  ['year', 60 * 60 * 24 * 365],
  ['month', 60 * 60 * 24 * 30],
  ['week', 60 * 60 * 24 * 7],
  ['day', 60 * 60 * 24],
  ['hour', 60 * 60],
  ['minute', 60],
];

/** "2h ago" / "in 20h" style relative time — used by Activity Log and Printer Detail's
 * recent-activity feed so both format timestamps identically. */
export function formatRelativeTime(
  isoTimestamp: string | null | undefined,
): string {
  if (!isoTimestamp) return '';
  const then = new Date(isoTimestamp).getTime();
  if (Number.isNaN(then)) return '';

  const diffSeconds = (then - Date.now()) / 1000;
  const absSeconds = Math.abs(diffSeconds);

  if (absSeconds < 60) return 'just now';

  for (const [unit, secondsInUnit] of UNITS) {
    if (absSeconds >= secondsInUnit) {
      const value = Math.round(diffSeconds / secondsInUnit);
      return RELATIVE_TIME_FORMAT.format(value, unit);
    }
  }

  return 'just now';
}
