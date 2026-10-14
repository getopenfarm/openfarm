import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { NonPrintedComponentFormPage } from './NonPrintedComponentFormPage';

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
      {
        path: '/non-printed-components/new',
        element: <NonPrintedComponentFormPage mode="add" />,
      },
      {
        path: '/non-printed-components',
        element: <div>Non-printed components catalog page</div>,
      },
    ],
    { initialEntries: ['/non-printed-components/new'] },
  );

  return render(<RouterProvider router={router} />, { wrapper: Wrapper });
}

describe('NonPrintedComponentFormPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('submits name/unit/unitCost on create', async () => {
    let capturedBody: unknown;
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (
          url.includes('/non-printed-components') &&
          init?.method === 'POST'
        ) {
          capturedBody = JSON.parse(init.body as string);
          return Promise.resolve(
            jsonResponse(
              { id: 'c1', name: 'M3x8 screw', unit: 'each', unitCost: 0.02 },
              201,
            ),
          );
        }
        return Promise.reject(new Error(`Unexpected fetch: ${url}`));
      }),
    );

    renderPage();

    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Name'), 'M3x8 screw');
    await user.type(screen.getByLabelText('Unit'), 'each');
    await user.type(screen.getByLabelText('Unit cost (optional)'), '0.02');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await vi.waitFor(() => {
      expect(capturedBody).toEqual({
        name: 'M3x8 screw',
        unit: 'each',
        unitCost: 0.02,
      });
    });
  });
});
