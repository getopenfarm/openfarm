import { getToken } from '@/lib/auth/tokenStorage';

const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8888';

// Orval's `httpClient: 'fetch'` mode expects the mutator to resolve to
// `{ data, status, headers }`, not the bare parsed body.
export async function httpClient<T>(
  url: string,
  options: RequestInit = {},
): Promise<T> {
  const token = getToken();

  const response = await fetch(`${API_BASE_URL}${url}`, {
    ...options,
    headers: {
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...options.headers,
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  });

  if (!response.ok) {
    throw response;
  }

  const contentType = response.headers.get('content-type');
  const data =
    response.status === 204 || !contentType?.includes('application/json')
      ? undefined
      : await response.json();

  return { data, status: response.status, headers: response.headers } as T;
}

export default httpClient;
