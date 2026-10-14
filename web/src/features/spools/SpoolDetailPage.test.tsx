import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { SpoolDetailPage } from './SpoolDetailPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const spool = {
  id: 'spool-1',
  label: 'Shelf A',
  colorName: 'Jade White',
  material: 'PLA',
  vendorName: 'Bambu',
  spoolWeightGrams: 1000,
  remainingWeightGrams: 800,
  state: 'Using',
  feedSlotLabel: 'AMS 1 · A1',
};

function renderPage(fetchMock: ReturnType<typeof vi.fn>) {
  vi.stubGlobal('fetch', fetchMock);
  const router = createMemoryRouter(
    [
      { path: '/spools/:id', element: <SpoolDetailPage /> },
      { path: '/spools', element: <div>Spools list page</div> },
    ],
    { initialEntries: ['/spools/spool-1'] },
  );
  render(
    <QueryClientProvider
      client={
        new QueryClient({
          defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
          },
        })
      }
    >
      <RouterProvider router={router} />
      <Toaster />
    </QueryClientProvider>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe('SpoolDetailPage', () => {
  it('saves label and remaining weight in one PUT', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'PUT') {
        const body = JSON.parse(init.body as string);
        return Promise.resolve(
          jsonResponse({
            ...spool,
            label: body.label,
            remainingWeightGrams: body.remainingWeightGrams,
          }),
        );
      }
      return Promise.resolve(jsonResponse({ spools: [spool] }));
    });
    renderPage(fetchMock);

    expect(
      await screen.findByRole('heading', { name: 'Shelf A' }),
    ).toBeInTheDocument();
    expect(screen.getByText('In use')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Record usage' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Remove from slot' }),
    ).toBeInTheDocument();
    const save = screen.getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();

    const weight = screen.getByLabelText('Remaining weight (g)');
    await user.clear(weight);
    await user.type(weight, '1200');
    await user.click(save);
    expect(
      await screen.findByText('Cannot exceed the spool capacity (1000 g).'),
    ).toBeInTheDocument();

    await user.clear(screen.getByLabelText('Label'));
    await user.clear(weight);
    await user.type(weight, '650');
    await user.click(save);

    expect(await screen.findByText('Saved.')).toBeInTheDocument();
    const puts = fetchMock.mock.calls.filter(
      ([, init]) => (init as RequestInit | undefined)?.method === 'PUT',
    );
    expect(puts).toHaveLength(1);
    expect(puts[0][0].toString()).toContain('/api/v1/spools/spool-1');
    expect(JSON.parse((puts[0][1] as RequestInit).body as string)).toEqual({
      label: null,
      remainingWeightGrams: 650,
    });
  });

  it('shows the server problem detail when the save is rejected', async () => {
    const user = userEvent.setup();
    renderPage(
      vi.fn((_input: RequestInfo | URL, init?: RequestInit) =>
        Promise.resolve(
          init?.method === 'PUT'
            ? jsonResponse({ detail: 'Weight exceeds capacity.' }, 422)
            : jsonResponse({ spools: [spool] }),
        ),
      ),
    );
    await user.type(await screen.findByLabelText('Label'), 'x');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(
      await screen.findByText('Weight exceeds capacity.'),
    ).toBeInTheDocument();
  });

  it('shows not found for an unknown spool', async () => {
    renderPage(vi.fn(() => Promise.resolve(jsonResponse({ spools: [] }))));
    expect(await screen.findByText('Spool not found')).toBeInTheDocument();
  });
});
