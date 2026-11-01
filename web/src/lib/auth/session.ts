import { apiUrl } from '@/lib/api/apiUrl';
import {
  clearToken,
  getToken,
  setToken,
  tokenExpiresSoon,
} from './tokenStorage';

let refreshPromise: Promise<string | null> | null = null;
let epoch = 0;
let channel: BroadcastChannel | null = null;
let channelUsers = 0;
export function connectSessionChannel() {
  channelUsers++;
  if (!channel && typeof BroadcastChannel !== 'undefined') {
    channel = new BroadcastChannel('openfarm.auth');
    channel.addEventListener('message', (event) => {
      if (event.data !== 'login' && event.data !== 'logout') return;
      epoch++;
      clearToken();
      window.dispatchEvent(new window.Event('openfarm:logout'));
      if (event.data === 'login')
        window.dispatchEvent(new window.Event('openfarm:login'));
    });
  }
  return () => {
    channelUsers--;
    if (channelUsers === 0) {
      channel?.close();
      channel = null;
    }
  };
}

export async function csrfPost(path: string) {
  const csrf = await fetch(apiUrl('/api/v1/auth/csrf'), {
    credentials: 'include',
    cache: 'no-store',
  });
  if (!csrf.ok) throw csrf;
  const { token } = await csrf.json();
  return fetch(apiUrl(path), {
    method: 'POST',
    credentials: 'include',
    headers: { 'X-CSRF-TOKEN': token },
  });
}

export function refreshSession(): Promise<string | null> {
  if (refreshPromise) return refreshPromise;
  const started = epoch;
  const refresh = async () => {
    const response = await csrfPost('/api/v1/auth/refresh');
    if (started !== epoch) return null;
    if (response.status === 401) {
      forgetSession();
      return null;
    }
    if (!response.ok) throw response;
    const data = await response.json();
    if (started !== epoch) return null;
    setToken(data.token);
    return data.token as string;
  };
  refreshPromise = (
    navigator.locks
      ? navigator.locks.request('openfarm.refresh', refresh)
      : refresh()
  ).finally(() => {
    refreshPromise = null;
  });
  return refreshPromise;
}

export async function validToken(): Promise<string | null> {
  return tokenExpiresSoon() ? refreshSession() : getToken();
}

export function acceptLogin(token: string) {
  epoch++;
  setToken(token);
  channel?.postMessage('login');
}

export async function endSession() {
  // Do not pretend logout succeeded if the server could not revoke the cookie.
  const response = await csrfPost('/api/v1/auth/logout');
  if (!response.ok) throw response;
  forgetSession();
}

export function forgetSession() {
  epoch++;
  clearToken();
  channel?.postMessage('logout');
  window.dispatchEvent(new window.Event('openfarm:logout'));
}

export async function authenticatedFetch(
  url: string,
  options: RequestInit = {},
) {
  const original = getToken();
  const token =
    original && tokenExpiresSoon() ? await refreshSession() : original;
  const request = (credential: string | null) =>
    fetch(apiUrl(url), {
      ...options,
      credentials: 'include',
      headers: {
        ...Object.fromEntries(new Headers(options.headers)),
        ...(credential ? { Authorization: `Bearer ${credential}` } : {}),
      },
    });
  let response = await request(token);
  if (
    response.status === 401 &&
    token &&
    !url.startsWith('/api/v1/auth/login')
  ) {
    const refreshed = await refreshSession();
    if (refreshed) response = await request(refreshed);
    if (!refreshed || response.status === 401) {
      forgetSession();
      if (window.location.pathname !== '/login')
        window.location.assign(
          `/login?returnTo=${encodeURIComponent(window.location.pathname + window.location.search)}`,
        );
    }
  }
  return response;
}
