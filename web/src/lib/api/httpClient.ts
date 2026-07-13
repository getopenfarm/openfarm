import { getToken } from '@/lib/auth/tokenStorage';

const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8888';

// Orval's `httpClient: 'fetch'` mode expects the mutator to resolve to
// `{ data, status, headers }`, not the bare parsed body.
const METHODS_EXPECTING_A_BODY = new Set(['POST', 'PUT', 'PATCH']);

export async function httpClient<T>(
  url: string,
  options: RequestInit = {},
): Promise<T> {
  const token = getToken();
  const method = options.method?.toUpperCase() ?? 'GET';
  // A bodyless mutation (e.g. mark-ready, driven entirely by the route) still needs a JSON
  // Content-Type — ASP.NET Core's minimal-API body binding returns 415 for POST/PUT/PATCH
  // requests with no Content-Type at all, even when the bound request DTO has no properties left
  // to send after route-bound fields are excluded.
  const body =
    options.body ?? (METHODS_EXPECTING_A_BODY.has(method) ? '{}' : undefined);

  const response = await fetch(`${API_BASE_URL}${url}`, {
    ...options,
    body,
    headers: {
      ...(body ? { 'Content-Type': 'application/json' } : {}),
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
