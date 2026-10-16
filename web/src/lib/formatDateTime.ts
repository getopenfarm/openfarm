const OPTIONS: Intl.DateTimeFormatOptions = {
  dateStyle: 'medium',
  timeStyle: 'short',
};

/** Locale date + time, e.g. "Oct 16, 2026, 3:04 PM". */
export const formatDateTime = (value: string | number | Date) =>
  new Date(value).toLocaleString(undefined, OPTIONS);
