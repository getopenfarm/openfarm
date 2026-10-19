/** Vite embeds this optional override at build time; empty means the current origin. */
export function apiUrl(path: string): string {
  const base = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '');
  return `${base}${path}`;
}
