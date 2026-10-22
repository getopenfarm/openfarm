const trim = (n: number, digits = 1) => String(Number(n.toFixed(digits)));

/** 250 -> "250 g"; keeps up to 1 decimal, trims trailing ".0". */
export const formatGrams = (n: number) => `${trim(n)} g`;

/** 250 -> "250 g"; 1000 -> "1 kg"; 2500 -> "2.5 kg". For totals that grow past a spool. */
export const formatWeight = (grams: number) =>
  grams >= 1000 ? `${trim(grams / 1000, 2)} kg` : formatGrams(grams);

/** 0.125 -> "0.13"; no currency symbol — the app has no currency setting. */
export const formatCost = (n: number) => n.toFixed(2);

/** 0.4 -> "0.4 mm"; keeps up to 2 decimals. */
export const formatMm = (n: number) => `${trim(n, 2)} mm`;

/** 215 -> "215 °C". */
export const formatCelsius = (n: number) => `${trim(n)} °C`;

/** KB below 1 MB (min "1 KB" for >0), else MB with 1 decimal. */
export function formatFileSize(bytes: number): string {
  if (bytes <= 0) return '0 KB';
  const mb = bytes / 1_048_576;
  return mb >= 1
    ? `${mb.toFixed(1)} MB`
    : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}
