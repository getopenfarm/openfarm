import { useQuery } from '@tanstack/react-query';
import { useContext } from 'react';
import { AccessContext } from './AccessContext';
import { apiQueryPermissions } from './apiPermissions.generated';

const rules = apiQueryPermissions.map((rule) => ({
  ...rule,
  expression: new RegExp(rule.pattern),
}));
export function queryIsAllowed(
  key: readonly unknown[],
  access: ReadonlySet<string> | undefined,
) {
  if (access === undefined || typeof key[0] !== 'string') return true;
  const path = key[0].split('?')[0];
  const rule = rules.find((rule) => rule.expression.test(path));
  if (!rule) return true; // Local cache keys are not HTTP endpoints.
  const permissions = [...rule.permissions];
  if (path.startsWith('/api/v1/library/parts')) permissions.push('parts:list');
  if (path.startsWith('/api/v1/library/assemblies'))
    permissions.push('assemblies:view');
  return permissions.every((key) => access.has(key));
}

// Preserve TanStack's overloads, including generated-client select and initialData types.
export const useAuthorizedQuery: typeof useQuery = ((options, client) => {
  const access = useContext(AccessContext);
  const allowed = queryIsAllowed(options.queryKey, access);
  return useQuery(
    { ...options, enabled: allowed ? options.enabled : false },
    client,
  );
}) as typeof useQuery;
