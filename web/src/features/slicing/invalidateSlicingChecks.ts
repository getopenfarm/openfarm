import type { QueryClient } from '@tanstack/react-query';

/**
 * Printer, filament and default-slicer changes decide what each slicer can print, so the readiness checks and the
 * queue's blocking reasons are stale the moment one is saved.
 */
export function invalidateSlicingChecks(queryClient: QueryClient) {
  return queryClient.invalidateQueries({
    predicate: ({ queryKey }) =>
      typeof queryKey[0] === 'string' &&
      (queryKey[0].startsWith('/api/v1/slicing') ||
        queryKey[0].startsWith('/api/v1/print-jobs')),
  });
}
