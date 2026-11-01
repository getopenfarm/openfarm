import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { usePartsHub } from './usePartsHub';

const { handlers } = vi.hoisted(() => ({
  handlers: new Map<string, (payload: { partId: string }) => void>(),
}));
vi.mock('@/lib/signalr/farmHubConnection', () => ({
  // Each handler is registered on the shared connection; the latest render's wins, as in useFarmHubEvent.
  useFarmHubEvent: (event: string, handler: (payload: never) => void) =>
    handlers.set(event, handler as never),
  useOnFarmHubReconnected: () => {},
}));
describe('Library event invalidation', () => {
  it('refreshes unified Parts and Assembly preview queries after part events', () => {
    const client = new QueryClient();
    const keys = [
      ['/api/v1/library', 'parts', 'contents'],
      ['/api/v1/library', 'assemblies', 'contents'],
      ['/api/v1/parts'],
    ];
    for (const key of keys) client.setQueryData(key, { items: [] });
    const { unmount } = renderHook(() => usePartsHub(), {
      wrapper: ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={client}>{children}</QueryClientProvider>
      ),
    });
    act(() => {
      handlers.get('PartUpserted')?.({ partId: 'a' });
    });
    for (const key of keys)
      expect(client.getQueryState(key)?.isInvalidated).toBe(true);
    unmount();
  });
});
