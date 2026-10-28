import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { NewProductionRunPage } from './NewProductionRunPage';

vi.mock('./hooks/useProductionHub', () => ({
  invalidateProductionQueries: () => {},
}));

const BRACKET = {
  id: 'part-1',
  name: 'Bracket',
  unitsPerPrint: 4,
  printRequirementsStatus: 'Complete',
  estimatedPrintTimeMinutes: 90,
  estimatedFilamentWeightGrams: 40,
};

afterEach(() => vi.unstubAllGlobals());

it('builds a run from picked parts, with a live summary, in one POST', async () => {
  const posts: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (init?.method === 'POST') {
        posts.push(JSON.parse(String(init.body)));
        return new Response(JSON.stringify({ id: 'run-1' }), { status: 201 });
      }
      const body = url.includes('/api/v1/parts?')
        ? { parts: [BRACKET], totalCount: 1 }
        : url.includes('/api/v1/parts/part-1')
          ? BRACKET
          : url.endsWith('/api/v1/printers')
            ? { printers: [{ id: 'p1', name: 'P01' }] }
            : { items: [], totalCount: 0 };
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }),
  );
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
      { path: '/production-runs/new', element: <NewProductionRunPage /> },
      { path: '/production-runs/:id', element: <p>Run page</p> },
    ],
    { initialEntries: ['/production-runs/new'] },
  );
  render(<RouterProvider router={router} />, { wrapper: Wrapper });
  const user = userEvent.setup();

  await user.type(screen.getByLabelText('Name'), 'Order #1042');
  await user.click(
    screen.getByRole('button', { name: 'Add part or assembly' }),
  );
  await user.click(await screen.findByRole('option', { name: /Bracket/ }));

  const units = await screen.findByLabelText('Units');
  expect(units).toHaveValue(4);
  await user.clear(units);
  await user.type(units, '10');
  const summary = within(
    screen.getByText('Summary').closest('[data-slot="card"]') as HTMLElement,
  );
  // 10 at 4 per plate rounds up to 3 plates, 12 units.
  expect(await summary.findByText('3')).toBeInTheDocument();
  expect(summary.getByText('12')).toBeInTheDocument();
  expect(summary.getByText('≈ 4h 30m')).toBeInTheDocument();

  await user.click(screen.getByRole('combobox', { name: 'Printer' }));
  await user.click(await screen.findByRole('option', { name: 'P01' }));
  await user.click(screen.getByRole('button', { name: 'Add run' }));

  expect(await screen.findByText('Run page')).toBeInTheDocument();
  expect(posts).toEqual([
    {
      name: 'Order #1042',
      dueDate: null,
      notes: null,
      lines: [
        {
          targetKind: 'Part',
          targetId: 'part-1',
          quantity: 10,
          assignmentMode: 'Pinned',
          pinnedPrinterId: 'p1',
        },
      ],
    },
  ]);
});

it('asks for at least one line before adding', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      async () =>
        new Response(JSON.stringify({ items: [], parts: [], totalCount: 0 }), {
          status: 200,
        }),
    ),
  );
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const router = createMemoryRouter([
    { path: '/', element: <NewProductionRunPage /> },
  ]);
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );

  await userEvent.click(screen.getByRole('button', { name: 'Add run' }));
  expect(
    await screen.findByText('Add at least one part or assembly.'),
  ).toBeInTheDocument();
});
