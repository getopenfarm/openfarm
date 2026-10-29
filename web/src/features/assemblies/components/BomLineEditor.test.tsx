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

  it('searches parts and assemblies, not catalog items, and adds the pick', async () => {
    stubFetch({
      '/api/v1/parts': {
        parts: [{ id: 'p1', name: 'Bracket' }],
        totalCount: 1,
      },
      '/api/v1/assemblies': { items: [{ id: 'a2', name: 'Hinge' }] },
      '/api/v1/cost-items': {
        items: [{ id: 'c1', name: 'M3 screw' }],
      },
    });

    // Plain http over a LAN IP is not a secure context: no crypto.randomUUID.
    vi.stubGlobal('crypto', {});
    const onChange = vi.fn();
    renderEditor([], onChange);

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /add line/i }));
    expect(
      await screen.findByRole('option', { name: 'Hinge' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('option', { name: /M3 screw/ }),
    ).not.toBeInTheDocument();

    await user.type(screen.getByRole('combobox'), 'brack');
    await user.click(screen.getByRole('option', { name: 'Bracket' }));

    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({
        targetKind: 'Part',
        targetId: 'p1',
        targetName: 'Bracket',
        quantity: 1,
      }),
    ]);
  });

  it('bumps the quantity when an already-listed target is picked again', async () => {
    stubFetch({
      '/api/v1/parts': {
        parts: [{ id: 'p1', name: 'Bracket' }],
        totalCount: 1,
      },
      '/api/v1/assemblies': { items: [] },
      '/api/v1/cost-items': { items: [] },
    });

    const onChange = vi.fn();
    renderEditor(
      [
        {
          key: 'line-1',
          targetKind: 'Part',
          targetId: 'p1',
          targetName: 'Bracket',
          quantity: 2,
        },
      ],
      onChange,
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /add line/i }));
    await user.click(await screen.findByRole('option', { name: 'Bracket' }));

    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ key: 'line-1', quantity: 3 }),
    ]);
  });

  it('removes an existing line', async () => {
    stubFetch({
      '/api/v1/parts': { parts: [], totalCount: 0 },
      '/api/v1/assemblies': { items: [], totalCount: 0 },
      '/api/v1/cost-items': { items: [], totalCount: 0 },
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

  it('counts components in whole pieces', async () => {
    stubFetch({
      '/api/v1/parts': { parts: [], totalCount: 0 },
      '/api/v1/assemblies': { items: [] },
      '/api/v1/cost-items': {
        items: [{ id: 'c2', name: 'M3 screw' }],
      },
    });

    renderEditor(
      [
        {
          key: 'l2',
          targetKind: 'NonPrintedComponent',
          targetId: 'c2',
          targetName: 'M3 screw',
          quantity: 4,
        },
      ],
      vi.fn(),
    );

    expect(await screen.findByText('Component')).toBeInTheDocument();
    const screw = screen.getByLabelText('Quantity for M3 screw');
    expect(screw).toHaveAttribute('step', '1');
    expect(screw).toHaveAttribute('min', '1');
  });
});
