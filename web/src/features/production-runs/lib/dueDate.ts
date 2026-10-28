const DAY_MS = 86_400_000;
const LABEL: Intl.DateTimeFormatOptions = {
  weekday: 'short',
  day: 'numeric',
  month: 'short',
};

/** Days from today to a `yyyy-mm-dd` due date, by calendar day in local time. */
export function daysUntil(dueDate: string, today = new Date()) {
  const [year, month, day] = dueDate.split('-').map(Number);
  const due = Date.UTC(year, month - 1, day);
  const now = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate());
  return Math.round((due - now) / DAY_MS);
}

/** "Fri, Oct 31" plus how far off it is; `overdue` and `soon` (within 2 days) pick the emphasis. */
export function describeDue(dueDate: string, today = new Date()) {
  const days = daysUntil(dueDate, today);
  const [year, month, day] = dueDate.split('-').map(Number);
  return {
    label: new Date(year, month - 1, day).toLocaleDateString(undefined, LABEL),
    relative:
      days === 0
        ? 'Today'
        : days === 1
          ? 'Tomorrow'
          : days > 0
            ? `In ${days} d`
            : `${-days} d overdue`,
    overdue: days < 0,
    soon: days >= 0 && days <= 2,
  };
}
