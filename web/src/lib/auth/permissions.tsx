import { useQuery } from '@tanstack/react-query';
import { type ReactNode, useContext } from 'react';
import { httpClient } from '@/lib/api/httpClient';
import { useAllowed } from './AccessContext';
import { AuthContext } from './AuthProvider';

export interface CurrentUser {
  userId: string;
  username: string;
  email: string;
  displayName?: string;
  isAdministrator: boolean;
  groups: string[];
  permissions: string[];
}
export function useIdentity() {
  const auth = useContext(AuthContext);
  return useQuery({
    queryKey: ['/api/v1/auth/whoami'],
    queryFn: () => httpClient<{ data: CurrentUser }>('/api/v1/auth/whoami'),
    select: (response) => response.data,
    enabled: !!auth?.isAuthenticated,
    staleTime: 15_000,
    refetchInterval: 30_000,
    retry: false,
  });
}
export function usePermissions() {
  const identity = useIdentity();
  return {
    ...identity,
    can: (key: string) => !!identity.data?.permissions?.includes(key),
  };
}
export function Can({
  permission,
  children,
}: {
  permission: string;
  children: ReactNode;
}) {
  return useAllowed(permission) ? children : null;
}
const settingsPaths = [
  '/settings',
  '/settings/fees',
  '/settings/printer-costs',
  '/settings/slicing-defaults',
];
/** First settings page the user can read, or null when none. */
export function settingsPath(can: (key: string) => boolean): string | null {
  return settingsPaths.find((path) => can(routePermission(path) ?? '')) ?? null;
}
export function routePermission(path: string): string | null {
  if (path === '/profile') return null;
  if (path.startsWith('/users-groups')) return 'users:manage';
  if (path.startsWith('/settings')) {
    if (path.includes('/fees')) return 'settings:fees:read';
    if (path.includes('/printer-costs')) return 'settings:printer-costs:read';
    if (path.includes('/slicing')) return 'settings:slicing-defaults:read';
    return 'settings:general:read';
  }
  const area = path.split('/')[1];
  const mutations: Record<string, string> = {
    printers: path.endsWith('/new')
      ? 'printers:register'
      : 'printers:edit-connection',
    parts: 'parts:create',
    assemblies: path.endsWith('/new') ? 'assemblies:create' : 'assemblies:edit',
    filament: path.endsWith('/new')
      ? 'filaments:create'
      : 'filaments:assign-profiles',
    spools: 'spools:register',
    'production-runs': path.endsWith('/new')
      ? 'production-runs:create'
      : 'production-runs:edit',
    'cost-catalog': 'non-printed-components:create',
    profiles: 'profiles:confirm-import',
  };
  if (
    (path.endsWith('/new') ||
      path.endsWith('/edit') ||
      path === '/profiles/import') &&
    mutations[area]
  )
    return mutations[area];
  const mapping: Record<string, string> = {
    '': 'printers:list',
    printers: 'printers:list',
    maintenance: 'printers:list',
    jobs: 'print-jobs:view',
    'print-jobs': 'print-jobs:view',
    'production-runs': 'production-runs:view',
    filament: 'filaments:list',
    spools: 'spools:list',
    parts: 'parts:list',
    assemblies: 'assemblies:view',
    'cost-catalog': 'non-printed-components:view',
    profiles: 'profiles:list-machine',
    stats: 'spools:list',
    'activity-log': 'activity-log:list',
  };
  return mapping[area] ?? null;
}
