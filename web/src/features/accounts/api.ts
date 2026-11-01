import {
  type QueryClient,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import type {
  AccountResponse,
  GroupResponse,
  PermissionArea,
  PermissionCatalogResponse,
  PermissionDefinition,
  ProfileInput,
  SessionResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';

export type {
  AccountResponse,
  GroupResponse,
  PermissionArea,
  PermissionDefinition,
  SessionResponse,
};
export const adminKey = '/api/v1/admin';
export function useAdministration() {
  return useQuery({
    queryKey: [adminKey],
    queryFn: async () => {
      const [users, groups, permissions] = await Promise.all([
        httpClient<{ data: AccountResponse[] }>(`${adminKey}/users`),
        httpClient<{ data: GroupResponse[] }>(`${adminKey}/groups`),
        httpClient<{ data: PermissionCatalogResponse }>(
          `${adminKey}/permissions`,
        ),
      ]);
      return {
        users: users.data,
        groups: groups.data,
        areas: permissions.data.areas ?? [],
        permissions: permissions.data.permissions ?? [],
      };
    },
  });
}

export const whoAmIKey = ['/api/v1/auth/whoami'];
const ownSessionsKey = ['/api/v1/auth/sessions'];
/** Own sessions without a userId; that user's sessions (users:manage) with one. */
export function sessionsKey(userId?: string) {
  return userId ? [adminKey, 'users', userId, 'sessions'] : ownSessionsKey;
}
export function useSessions(userId?: string) {
  return useQuery({
    queryKey: sessionsKey(userId),
    queryFn: async () =>
      (
        await httpClient<{ data: SessionResponse[] }>(
          userId
            ? `${adminKey}/users/${userId}/sessions`
            : '/api/v1/auth/sessions',
        )
      ).data,
  });
}
export function useRevokeSession(userId?: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (sessionId: string) =>
      httpClient(
        userId
          ? `${adminKey}/users/${userId}/sessions/${sessionId}`
          : `/api/v1/auth/sessions/${sessionId}`,
        { method: 'DELETE' },
      ),
    onSuccess: () => invalidateSessions(client, userId),
  });
}
export function useRevokeAllSessions(userId?: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () =>
      userId
        ? httpClient(`${adminKey}/users/${userId}/sessions`, {
            method: 'DELETE',
          })
        : httpClient('/api/v1/auth/sessions/revoke-all', { method: 'POST' }),
    onSuccess: () => invalidateSessions(client, userId),
  });
}
function invalidateSessions(client: QueryClient, userId?: string) {
  // [adminKey] prefixes the user's session list and the user list's sessionCount.
  return client.invalidateQueries({
    queryKey: userId ? [adminKey] : ownSessionsKey,
  });
}
export function useRemoveUser() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      httpClient(`${adminKey}/users/${id}`, { method: 'DELETE' }),
    onSuccess: () => client.invalidateQueries({ queryKey: [adminKey] }),
  });
}
export function useUpdateProfile() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: ProfileInput) =>
      httpClient('/api/v1/auth/profile', {
        method: 'PUT',
        body: JSON.stringify(input),
      }),
    onSuccess: () => client.invalidateQueries({ queryKey: whoAmIKey }),
  });
}
