import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AssemblyDetailPage } from './AssemblyDetailPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const ASSEMBLY = {
  id: 'a1',
  name: 'Enclosure',
  description: null,
  lines: [
    {
      id: 'l1',
      targetKind: 'Part',
      targetId: 'p1',
      targetName: 'Bracket',
      quantity: 2,
    },
    {
      id: 'l2',
      targetKind: 'Part',
      targetId: 'p2',
      targetName: 'Lid',
      quantity: 1,
    },
  ],
};

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }
  const router = createMemoryRouter(
    [{ path: '/assemblies/:id', element: <AssemblyDetailPage /> }],
    { initialEntries: ['/assemblies/a1'] },
  );
  return render(<RouterProvider router={router} />, { wrapper: Wrapper });
}

describe('AssemblyDetailPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('saves name and line changes in one PUT with the full line list', async () => {
    const puts: unknown[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = input.toString();
        if (url.endsWith('/api/v1/assemblies/a1') && init?.method === 'PUT') {
          puts.push(JSON.parse(init.body as string));
          return Promise.resolve(jsonResponse(ASSEMBLY));
        }
        if (url.endsWith('/api/v1/assemblies/a1')) {
          return Promise.resolve(jsonResponse(ASSEMBLY));
        }
        if (url.includes('/api/v1/parts')) {
          return Promise.resolve(jsonResponse({ parts: [], totalCount: 0 }));
        }
        return Promise.resolve(jsonResponse({ items: [], totalCount: 0 }));
      }),
    );

    renderPage();
    const user = userEvent.setup();

    const name = await screen.findByLabelText('Name');
    const save = screen.getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();
    expect(
      screen.queryByRole('button', { name: /edit/i }),
    ).not.toBeInTheDocument();

    await user.clear(name);
    await user.type(name, 'Big enclosure');
    const qty = screen.getByLabelText('Quantity for Bracket');
    await user.clear(qty);
    await user.type(qty, '3');
    await user.click(screen.getByRole('button', { name: 'Remove Lid' }));
    await user.click(save);

    await vi.waitFor(() => expect(puts).toHaveLength(1));
    expect(puts[0]).toEqual({
      additionalCosts: [],
      pricing: null,
      name: 'Big enclosure',
      description: null,
      lines: [
        { lineId: 'l1', targetKind: 'Part', targetId: 'p1', quantity: 3 },
      ],
    });
  });
});
