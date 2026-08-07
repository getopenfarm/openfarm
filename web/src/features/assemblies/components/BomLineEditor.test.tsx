import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { BomLineEditor, type EditableLine } from './BomLineEditor';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFetch(routes: Record<string, unknown>) {
  const sortedRoutes = Object.entries(routes).sort(
    ([a], [b]) => b.length - a.length,
  );
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      for (const [path, body] of sortedRoutes) {
        if (url.includes(path)) {
          return Promise.resolve(jsonResponse(body));
        }
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    }),
  );
}

function renderEditor(
  lines: EditableLine[],
  onChange: (l: EditableLine[]) => void,
) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }

  return render(<BomLineEditor lines={lines} onChange={onChange} />, {
    wrapper: Wrapper,
  });
}

describe('BomLineEditor', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('offers Part, Assembly, and NonPrintedComponent as target kinds', async () => {
    stubFetch({
      '/api/v1/parts': { parts: [], totalCount: 0 },
      '/api/v1/assemblies': { items: [], totalCount: 0 },
      '/api/v1/non-printed-components': { items: [], totalCount: 0 },
    });

    const onChange = vi.fn();
    renderEditor([], onChange);

    const user = userEvent.setup();
    await user.click(
      await screen.findByRole('combobox', { name: 'Target kind' }),
    );

    expect(
      await screen.findByText('Non-printed component'),
    ).toBeInTheDocument();
  });

  it('adds a line for the chosen target and calls onChange', async () => {
    stubFetch({
      '/api/v1/parts': {
        parts: [{ id: 'p1', name: 'Bracket' }],
        totalCount: 1,
      },
      '/api/v1/assemblies': { items: [], totalCount: 0 },
      '/api/v1/non-printed-components': { items: [], totalCount: 0 },
    });

    const onChange = vi.fn();
    renderEditor([], onChange);

    const user = userEvent.setup();
    await user.click(await screen.findByRole('combobox', { name: 'Target' }));
    await user.click(await screen.findByRole('option', { name: 'Bracket' }));
    await user.click(screen.getByRole('button', { name: /add line/i }));

    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({
        targetKind: 'Part',
        targetId: 'p1',
        targetName: 'Bracket',
        quantity: 1,
      }),
    ]);
  });

  it('removes an existing line', async () => {
    stubFetch({
      '/api/v1/parts': { parts: [], totalCount: 0 },
      '/api/v1/assemblies': { items: [], totalCount: 0 },
      '/api/v1/non-printed-components': { items: [], totalCount: 0 },
    });

    const onChange = vi.fn();
    const existingLine: EditableLine = {
      key: 'line-1',
      lineId: 'line-1',
      targetKind: 'Part',
      targetId: 'p1',
      targetName: 'Bracket',
      quantity: 2,
    };
    renderEditor([existingLine], onChange);

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: 'Remove Bracket' }));

    expect(onChange).toHaveBeenCalledWith([]);
  });
});
