import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AssemblyFormPage } from './AssemblyFormPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

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
    [
      { path: '/assemblies/new', element: <AssemblyFormPage /> },
      { path: '/assemblies', element: <div>Assemblies catalog page</div> },
      { path: '/assemblies/:id', element: <div>Assembly detail page</div> },
    ],
    { initialEntries: ['/assemblies/new'] },
  );

  return render(<RouterProvider router={router} />, { wrapper: Wrapper });
}

describe('AssemblyFormPage (add)', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('builds an assembly locally and submits it in a single create call', async () => {
    let capturedBody: unknown;
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/api/v1/parts')) {
          return Promise.resolve(
            jsonResponse({
              parts: [{ id: 'p1', name: 'Bracket' }],
              totalCount: 1,
            }),
          );
        }
        if (url.includes('/api/v1/assemblies') && init?.method === 'POST') {
          capturedBody = JSON.parse(init.body as string);
          return Promise.resolve(
            jsonResponse(
              { id: 'a1', name: 'Enclosure', description: null, lines: [] },
              201,
            ),
          );
        }
        if (url.includes('/api/v1/assemblies')) {
          return Promise.resolve(jsonResponse({ items: [], totalCount: 0 }));
        }
        if (url.includes('/api/v1/non-printed-components')) {
          return Promise.resolve(jsonResponse({ items: [], totalCount: 0 }));
        }
        return Promise.reject(new Error(`Unexpected fetch: ${url}`));
      }),
    );

    renderPage();

    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Name'), 'Enclosure');

    await user.click(screen.getByRole('button', { name: /add line/i }));
    await user.click(await screen.findByRole('option', { name: 'Bracket' }));

    await user.click(screen.getByRole('button', { name: 'Add assembly' }));

    await vi.waitFor(() => {
      expect(capturedBody).toEqual({
        name: 'Enclosure',
        description: null,
        lines: [{ targetKind: 'Part', targetId: 'p1', quantity: 1 }],
      });
    });
  });
});
