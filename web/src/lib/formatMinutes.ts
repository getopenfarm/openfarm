// Small shared formatter for minute counts as "Xh Ym" — used by the Parts
// feature (estimated print time) and the shared print-queue dialog
// (estimated queue time), so it lives at the top-level lib rather than
// inside a single feature.
export function formatMinutes(totalMinutes: number): string {
  const minutes = Math.max(0, Math.round(totalMinutes));
  const hours = Math.floor(minutes / 60);
  const remainderMinutes = minutes % 60;

  if (hours === 0) {
    return `${remainderMinutes}m`;
  }
  if (remainderMinutes === 0) {
    return `${hours}h`;
  }
  return `${hours}h ${remainderMinutes}m`;
}
