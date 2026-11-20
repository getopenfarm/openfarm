// An AI assistant's sign-in arrives as `/connect?request=<long payload>`. Sending that whole URL through
// the sign-in round-trip (`returnTo`, then the SSO `state`) can exceed the length caps, so the payload waits
// in sessionStorage and only a short `/connect?resume=<id>` travels. sessionStorage survives the trip to
// the identity provider and back in the same tab.
const KEY = 'openfarm.connect.pending';

interface Pending {
  id: string;
  request: string;
}

function read(): Pending | null {
  try {
    const value = JSON.parse(sessionStorage.getItem(KEY) ?? 'null');
    return typeof value?.id === 'string' && typeof value?.request === 'string'
      ? value
      : null;
  } catch {
    return null;
  }
}

function randomId() {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

/** The URL to return to after sign-in: short when the request can be stashed, otherwise unchanged. */
export function connectReturnTo(pathAndSearch: string): string {
  try {
    const url = new URL(pathAndSearch, 'http://localhost');
    const request = url.searchParams.get('request');
    if (url.pathname !== '/connect' || !request) return pathAndSearch;
    // Render-safe: the same request keeps the same id.
    const existing = read();
    const id = existing?.request === request ? existing.id : randomId();
    if (existing?.id !== id)
      sessionStorage.setItem(KEY, JSON.stringify({ id, request }));
    return `/connect?resume=${id}`;
  } catch {
    return pathAndSearch;
  }
}

/** The stashed consent request for this resume id, or null when it is unknown or storage is unavailable. */
export function resolveConnectRequest(resume: string | null): string | null {
  const pending = read();
  return resume && pending?.id === resume ? pending.request : null;
}

export function clearConnectRequest() {
  try {
    sessionStorage.removeItem(KEY);
  } catch {
    // Storage is unavailable; nothing to clear.
  }
}
