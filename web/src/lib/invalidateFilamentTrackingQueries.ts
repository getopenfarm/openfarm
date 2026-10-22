import type { QueryClient } from '@tanstack/react-query';

/**
 * Refetches everything that shows spool state or Feed unit topology: the spools list (/api/v1/spools), one spool and its
 * history (/api/v1/spools/{id}…), the printer-scoped Feed units (/api/v1/printers/{id}/feed-units) and the filament list,
 * whose stock totals follow spool weights. They are shown together on FeedUnitsCard, SpoolsTable, the spool page and the
 * filament catalog, and the by-id keys do not share the list key's prefix, so one predicate covers them all.
 */
export function invalidateFilamentTrackingQueries(queryClient: QueryClient) {
  return queryClient.invalidateQueries({
    predicate: (query) =>
      typeof query.queryKey[0] === 'string' &&
      (query.queryKey[0].includes('/feed-units') ||
        query.queryKey[0].startsWith('/api/v1/spools') ||
        query.queryKey[0] === '/api/v1/filaments'),
  });
}
