import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useBomCatalog } from './useBomCatalog';

const json = (data: unknown, status = 200) =>
  new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
function wrapper({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider
      client={
        new QueryClient({ defaultOptions: { queries: { retry: false } } })
      }
    >
      {children}
    </QueryClientProvider>
  );
}
afterEach(() => vi.unstubAllGlobals());

describe('useBomCatalog', () => {
  it('loads all catalog pages with supported sizes', async () => {
    const requests: URL[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = new URL(input.toString(), 'http://localhost');
        requests.push(url);
        const key = url.pathname.endsWith('/parts') ? 'parts' : 'items';
        const page = Number(url.searchParams.get('page'));
        return Promise.resolve(
          json({
            [key]:
              page === 1
                ? Array.from({ length: 200 }, (_, i) => ({ id: `first-${i}` }))
                : [{ id: 'last' }],
            totalCount: 201,
          }),
        );
      }),
    );
    const { result } = renderHook(() => useBomCatalog(), { wrapper });
    await waitFor(() => expect(result.current.isPending).toBe(false));
    expect(result.current.isError).toBe(false);
    expect(result.current.parts).toHaveLength(201);
    expect(result.current.assemblies).toHaveLength(201);
    expect(result.current.components).toHaveLength(201);
    expect(result.current.parts.at(-1)?.id).toBe('last');
    expect(requests).toHaveLength(6);
    expect(
      requests.every((url) => url.searchParams.get('pageSize') === '200'),
    ).toBe(true);
  });

  it('keeps a later page failure incomplete and retries all pages', async () => {
    let failed = true;
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = new URL(input.toString(), 'http://localhost');
        if (!url.pathname.endsWith('/parts'))
          return Promise.resolve(json({ items: [] }));
        if (url.searchParams.get('page') === '1')
          return Promise.resolve(
            json({ parts: [{ id: 'first' }], totalCount: 2 }),
          );
        return Promise.resolve(
          failed
            ? json({}, 500)
            : json({ parts: [{ id: 'last' }], totalCount: 2 }),
        );
      }),
    );
    const { result } = renderHook(() => useBomCatalog(), { wrapper });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.parts).toEqual([]);
    failed = false;
    result.current.retry();
    await waitFor(() => expect(result.current.parts).toHaveLength(2));
    expect(result.current.isError).toBe(false);
  });
});
