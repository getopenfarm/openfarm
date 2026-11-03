import { useQuery } from '@tanstack/react-query';
import type {
  AccessTokenCreated,
  AccessTokenInput,
  AccessTokenView,
  TokenOptionsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';

export type { AccessTokenCreated, AccessTokenView };
export const tokenUrl = (userId?: string) =>
  userId ? `/api/v1/admin/users/${userId}/tokens` : '/api/v1/auth/tokens';
export const tokenKey = (userId?: string) => [tokenUrl(userId)];
export function useTokens(userId?: string) {
  return useQuery({
    queryKey: tokenKey(userId),
    queryFn: async () =>
      (await httpClient<{ data: AccessTokenView[] }>(tokenUrl(userId))).data,
  });
}
export function useTokenOptions() {
  return useQuery({
    queryKey: ['/api/v1/auth/tokens/options'],
    queryFn: async () =>
      (
        await httpClient<{ data: TokenOptionsResponse }>(
          '/api/v1/auth/tokens/options',
        )
      ).data,
  });
}
// Credentials deliberately stay out of TanStack's query/mutation caches and browser storage.
export async function createToken(input: AccessTokenInput) {
  return (
    await httpClient<{ data: AccessTokenCreated }>(tokenUrl(), {
      method: 'POST',
      body: JSON.stringify(input),
    })
  ).data;
}
export async function regenerateToken(id: string) {
  return (
    await httpClient<{ data: AccessTokenCreated }>(
      `${tokenUrl()}/${id}/regenerate`,
      { method: 'POST' },
    )
  ).data;
}
export function revokeToken(id: string | null, userId?: string) {
  return httpClient(
    id
      ? `${tokenUrl(userId)}/${id}`
      : userId
        ? tokenUrl(userId)
        : `${tokenUrl()}/revoke-all`,
    { method: id || userId ? 'DELETE' : 'POST' },
  );
}

/** FastEndpoints validation errors carry a reason per item in the problem response. */
export async function tokenError(
  error: unknown,
  fallback: string,
): Promise<string> {
  if (error instanceof Response) {
    try {
      const problem = await error.clone().json();
      if (typeof problem.detail === 'string' && problem.detail)
        return problem.detail;
      if (Array.isArray(problem.errors)) {
        const reasons = problem.errors
          .map((item: { reason?: unknown }) => item.reason)
          .filter(
            (reason: unknown): reason is string => typeof reason === 'string',
          );
        if (reasons.length) return reasons.join(' ');
      }
    } catch {
      /* A network or non-JSON failure uses the contextual fallback. */
    }
  }
  return fallback;
}
