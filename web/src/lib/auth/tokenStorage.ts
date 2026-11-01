// Access tokens live only in memory. The persistent credential is an HttpOnly cookie.
let token: string | null = null;
const listeners = new Set<() => void>();
export function getToken(): string | null {
  return token;
}
export function setToken(value: string): void {
  token = value;
  localStorage.removeItem('openfarm.auth.token');
  for (const listener of listeners) listener();
}
export function clearToken(): void {
  token = null;
  localStorage.removeItem('openfarm.auth.token');
  for (const listener of listeners) listener();
}
export function subscribeToken(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}
export function tokenExpiresSoon(): boolean {
  if (!token) return true;
  try {
    return (
      JSON.parse(atob(token.split('.')[1])).exp * 1000 < Date.now() + 60_000
    );
  } catch {
    return false;
  }
}
