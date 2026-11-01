import type { HubConnection } from '@microsoft/signalr';
import { renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { expect, it, vi } from 'vitest';
import { FarmHubContext, useFarmHubEvent } from './farmHubConnection';

it('registers on the shared connection, calls the latest handler, and unregisters on unmount', () => {
  const listeners = new Map<string, (payload: string) => void>();
  const connection = {
    on: vi.fn((event: string, listener: (payload: string) => void) =>
      listeners.set(event, listener),
    ),
    off: vi.fn((event: string) => listeners.delete(event)),
  } as unknown as HubConnection;
  const first = vi.fn();
  const latest = vi.fn();
  const { rerender, unmount } = renderHook(
    ({ handler }) => useFarmHubEvent('PartDeleted', handler),
    {
      initialProps: { handler: first },
      wrapper: ({ children }: { children: ReactNode }) => (
        <FarmHubContext value={connection}>{children}</FarmHubContext>
      ),
    },
  );
  rerender({ handler: latest });
  listeners.get('PartDeleted')?.('part-1');
  expect(first).not.toHaveBeenCalled();
  expect(latest).toHaveBeenCalledWith('part-1');
  expect(connection.on).toHaveBeenCalledTimes(1);
  unmount();
  expect(listeners.has('PartDeleted')).toBe(false);
});
