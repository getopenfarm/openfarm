const OPTIONS: Intl.DateTimeFormatOptions = {
  dateStyle: 'medium',
  timeStyle: 'short',
};

/** Locale date + time, e.g. "Oct 16, 2026, 3:04 PM". */
export const formatDateTime = (value: string | number | Date) =>
  new Date(value).toLocaleString(undefined, OPTIONS);

const SHORT: Intl.DateTimeFormatOptions = {
  month: 'short',
  day: 'numeric',
  hour: 'numeric',
  minute: '2-digit',
};

/** Locale date + time without the year, e.g. "Oct 16, 3:04 PM": for recent events listed together. */
export const formatShortDateTime = (value: string | number | Date) =>
  new Date(value).toLocaleString(undefined, SHORT);
