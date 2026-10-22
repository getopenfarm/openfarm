/** A span of local calendar days, both ends included, as `<input type="date">` values ('YYYY-MM-DD'). */
export interface DaySpan {
  start: string;
  end: string;
}

/** The 'YYYY-MM-DD' of the local calendar day `date` falls on. */
export function localDay(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** The last `days` local days, today included. */
export function lastDays(days: number, now = new Date()): DaySpan {
  return {
    start: localDay(
      new Date(now.getFullYear(), now.getMonth(), now.getDate() - days + 1),
    ),
    end: localDay(now),
  };
}

/** The UTC instants the API filters on: from the first day's local midnight up to, not including, the midnight after the last. */
export function toInstants({ start, end }: DaySpan) {
  const midnight = (day: string, offset = 0) => {
    const [year, month, date] = day.split('-').map(Number);
    return new Date(year, month - 1, date + offset).toISOString();
  };
  return { from: midnight(start), to: midnight(end, 1) };
}
