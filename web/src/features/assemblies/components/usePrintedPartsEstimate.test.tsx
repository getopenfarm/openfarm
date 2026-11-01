import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import { usePrintedPartsEstimate } from './usePrintedPartsEstimate';

const json = (value: number) =>
  new Response(
    JSON.stringify({
      time: { value, missing: 0 },
      filament: { value: 2, missing: 0 },
      approximate: true,
      colors: [],
    }),
    { headers: { 'Content-Type': 'application/json' } },
  );
afterEach(() => vi.unstubAllGlobals());
it('sends draft quantities without saving and ignores an older quantity response', async () => {
  let finishOld!: (response: Response) => void;
  const fetchMock = vi.fn(
    async (_input: RequestInfo | URL, init?: RequestInit) => {
      const body = JSON.parse(String(init?.body));
      return body.parts[0].quantity === 1
        ? new Promise<Response>((resolve) => {
            finishOld = resolve;
          })
        : json(6);
    },
  );
  vi.stubGlobal('fetch', fetchMock);
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const { result, rerender } = renderHook(
    ({ quantity }) =>
      usePrintedPartsEstimate(new Map([['part-1', quantity]]), true),
    {
      initialProps: { quantity: 1 },
      wrapper: ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={client}>{children}</QueryClientProvider>
      ),
    },
  );
  await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
  rerender({ quantity: 2 });
  await waitFor(() => expect(result.current.data?.time?.value).toBe(6));
  await act(async () => finishOld(json(3)));
  expect(result.current.data?.time?.value).toBe(6);
  expect(fetchMock.mock.calls.map(([url]) => url)).toEqual([
    '/api/v1/parts/print-estimate',
    '/api/v1/parts/print-estimate',
  ]);
});
