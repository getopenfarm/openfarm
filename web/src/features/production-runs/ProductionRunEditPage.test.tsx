import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { ProductionRunEditPage } from './ProductionRunEditPage';

afterEach(() => vi.unstubAllGlobals());

const RUN = {
  id: 'r1',
  displayLabel: 'Weekly',
  name: 'Weekly',
  dueDate: null,
  notes: null,
  status: 'Active',
  lines: [
    {
      id: 'finite',
      targetKind: 'Part',
      targetName: 'Bracket',
      quantity: 10,
    },
    {
      id: 'u1',
      targetKind: 'Part',
      targetName: 'Clip',
      quantity: null,
      backfillPrinterCap: 1,
      backfillCumulativeUnitCeiling: null,
    },
    {
      id: 'u2',
      targetKind: 'Assembly',
      targetName: 'Hinge',
      quantity: null,
      backfillPrinterCap: 2,
      backfillCumulativeUnitCeiling: 50,
    },
  ],
};

it('saves the details and every unlimited line in one PUT', async () => {
  const puts: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'PUT') {
        expect(input.toString()).toMatch(/\/api\/v1\/production-runs\/r1$/);
        puts.push(JSON.parse(init.body as string));
      }
      return Promise.resolve(
        new Response(JSON.stringify(RUN), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
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
      { path: '/production-runs/:id/edit', element: <ProductionRunEditPage /> },
      { path: '/production-runs/:id', element: <p>Run page</p> },
    ],
    { initialEntries: ['/production-runs/r1/edit'] },
  );
  render(<RouterProvider router={router} />, { wrapper: Wrapper });

  const user = userEvent.setup();
  const caps = await screen.findAllByLabelText('Printers at once');
  expect(caps).toHaveLength(2);
  expect(screen.queryByText('Bracket')).not.toBeInTheDocument();
  const save = screen.getByRole('button', { name: 'Save' });
  expect(save).toBeDisabled();

  await user.clear(caps[0]);
  await user.type(caps[0], '3');
  const ceilings = screen.getAllByLabelText('Stop at units');
  await user.clear(ceilings[1]);
  await user.clear(screen.getByLabelText('Name'));
  await user.type(screen.getByLabelText('Name'), ' Order #7 ');
  await user.click(save);

  await vi.waitFor(() => expect(puts).toHaveLength(1));
  expect(puts[0]).toEqual({
    name: 'Order #7',
    dueDate: null,
    notes: null,
    lines: [
      {
        lineId: 'u1',
        backfillPrinterCap: 3,
        backfillCumulativeUnitCeiling: null,
      },
      {
        lineId: 'u2',
        backfillPrinterCap: 2,
        backfillCumulativeUnitCeiling: null,
      },
    ],
  });
  expect(await screen.findByText('Run page')).toBeInTheDocument();
});
