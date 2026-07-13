import { QueryClient } from '@tanstack/react-query';

// The 401 → clear-token-and-redirect handling lives in httpClient.ts, at the point the raw
// Response is read, rather than here via QueryCache.onError. TanStack Query only calls
// onError once a query reaches a terminal "error" status, but a fetch that gets cancelled
// (e.g. React StrictMode's mount → cleanup → remount in dev aborting the first attempt) can
// leave a query permanently stuck in limbo — neither loading, erroring, nor holding data —
// without ever reaching that terminal state. Reacting to the response itself guarantees the
// redirect fires for every real 401 regardless of what the query's retry bookkeeping does.
export const queryClient = new QueryClient();
