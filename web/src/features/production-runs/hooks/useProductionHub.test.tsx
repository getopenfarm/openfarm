import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { expect, it, vi } from 'vitest';
import { useProductionHub } from './useProductionHub';

const mock = vi.hoisted(() => ({
  handlers: new Map<string, () => void>(),
  stop: vi.fn(),
}));
vi.mock('@/lib/signalr/farmHubConnection', () => ({
  connectFarmHub: () => ({
    connection: {
      on: (event: string, handler: () => void) =>
        mock.handlers.set(event, handler),
    },
    stop: mock.stop,
  }),
}));

it('refreshes job and run capabilities when a library part is removed', () => {
  const client = new QueryClient();
  const jobKey = ['/api/v1/print-jobs/job-1'];
  const runKey = ['/api/v1/production-runs/run-1'];
  const otherKey = ['/api/v1/printers'];
  for (const key of [jobKey, runKey, otherKey]) client.setQueryData(key, {});
  const { unmount } = renderHook(() => useProductionHub(), {
    wrapper: ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  });
  act(() => mock.handlers.get('PartDeleted')?.());
  expect(client.getQueryState(jobKey)?.isInvalidated).toBe(true);
  expect(client.getQueryState(runKey)?.isInvalidated).toBe(true);
  expect(client.getQueryState(otherKey)?.isInvalidated).toBe(false);
  unmount();
  expect(mock.stop).toHaveBeenCalled();
});
