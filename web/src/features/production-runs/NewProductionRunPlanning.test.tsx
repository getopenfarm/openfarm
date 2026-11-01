import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { sumPrintEstimates } from '@/lib/printEstimates';
import type { PlanLine, ProductionPlan } from './components/productionPlanner';
import { NewProductionRunPage } from './NewProductionRunPage';

vi.mock('./hooks/useProductionHub', () => ({
  invalidateProductionQueries: vi.fn(),
}));
afterEach(() => vi.unstubAllGlobals());
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });

// The real planner owns BOM expansion, including nested/repeated components.
// These responses deliberately represent unequal quantities and exact remainder jobs.
function makePlan(line: PlanLine): ProductionPlan {
  const quantity = line.quantity ?? 1;
  const assembly = line.targetKind === 'Assembly';
  const parts = (
    assembly
      ? [
          { partId: 'bracket', name: 'Bracket', perSet: 2, capacity: 6 },
          { partId: 'base', name: 'Base', perSet: 1, capacity: 4 },
        ]
      : [{ partId: 'bracket', name: 'Bracket', perSet: 1, capacity: 6 }]
  ).map(({ partId, name, perSet, capacity }) => {
    const requiredUnits =
      line.quantityBasis === 'Plates' ? quantity * capacity : quantity * perSet;
    const plates = Math.ceil(requiredUnits / capacity);
    const jobUnits = Array.from({ length: plates }, (_, index) =>
      Math.min(capacity, requiredUnits - index * capacity),
    );
    return {
      partId,
      name,
      requiredUnits,
      plannedUnits: requiredUnits,
      unitsPerPlate: capacity,
      plates,
      surplusUnits: 0,
      estimate: {
        time: { value: (requiredUnits / capacity) * 60, missing: 0 },
        filament: { value: (requiredUnits / capacity) * 20, missing: 0 },
        approximate: requiredUnits % capacity !== 0,
        colors: [],
      },
      minutes: (requiredUnits / capacity) * 60,
      grams: (requiredUnits / capacity) * 20,
      jobUnits,
      revision: 'r1',
      updatedAtUtc: '',
      objectMappingVersion: null,
    };
  });
  return {
    estimate: sumPrintEstimates(parts.map((part) => part.estimate)),
    version: `${line.targetId}-${line.quantityBasis}-${quantity}`,
    parts,
    plates: parts.reduce((sum, part) => sum + part.plates, 0),
    completeSets: assembly ? quantity : null,
    minutes: null,
    grams: null,
  };
}
function setup(
  options: {
    preview?: (line: PlanLine) => Promise<Response> | Response;
    submit?: (body: unknown) => Promise<Response> | Response;
    catalogFails?: () => boolean;
  } = {},
) {
  const posts: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (url.includes('/production-demands/preview')) {
        const { line } = JSON.parse(String(init?.body));
        return options.preview ? options.preview(line) : json(makePlan(line));
      }
      if (init?.method === 'POST') {
        const body = JSON.parse(String(init.body));
        posts.push(body);
        return options.submit
          ? options.submit(body)
          : json({ id: 'new-run' }, 201);
      }
      if (options.catalogFails?.()) return json({ detail: 'Unavailable' }, 503);
      return json(
        url.includes('/parts?')
          ? {
              parts: [
                {
                  id: 'bracket',
                  name: 'Bracket',
                  unitsPerPrint: 6,
                  printRequirementsStatus: 'Complete',
                },
              ],
              totalCount: 1,
            }
          : url.includes('/assemblies?')
            ? {
                items: [
                  { id: 'kit', name: 'Desk kit' },
                  { id: 'nested', name: 'Nested kit' },
                ],
                totalCount: 2,
              }
            : url.endsWith('/printers')
              ? { printers: [] }
              : { items: [], totalCount: 0 },
      );
    }),
  );
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const router = createMemoryRouter(
    [
      { path: '/production-runs/new', element: <NewProductionRunPage /> },
      { path: '/production-runs/:id', element: <p>Saved run</p> },
    ],
    { initialEntries: ['/production-runs/new'] },
  );
  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  const user = userEvent.setup();
  async function pick(name: string) {
    await user.click(
      screen.getByRole('button', { name: 'Add part or assembly' }),
    );
    await user.click(
      await screen.findByRole('option', { name: new RegExp(name) }),
    );
  }
  const summary = () =>
    within(
      screen.getByText('Summary').closest('[data-slot="card"]') as HTMLElement,
    );
  return { user, pick, summary, posts };
}

it('shows unequal component requirements, exact partial plates, and sets separately from units', async () => {
  const { user, pick, summary, posts } = setup();
  await pick('Desk kit');
  const sets = screen.getByLabelText('Sets');
  await user.clear(sets);
  await user.type(sets, '10');
  const components = await screen.findByRole('region', {
    name: 'Components for Desk kit',
  });
  await waitFor(() =>
    expect(within(components).getByText('20')).toBeInTheDocument(),
  );
  const rows = within(components).getAllByRole('row');
  expect(
    within(rows[1])
      .getAllByRole('cell')
      .map((cell) => cell.textContent),
  ).toEqual(['Bracket', '2', '20', '6', '4']);
  expect(
    within(rows[2])
      .getAllByRole('cell')
      .map((cell) => cell.textContent),
  ).toEqual(['Base', '1', '10', '4', '3']);
  expect(summary().getByText('10 sets')).toBeInTheDocument();
  expect(summary().getByText('30')).toBeInTheDocument();
  expect(summary().getByText('7')).toBeInTheDocument();
  expect(summary().queryByText('Unavailable')).not.toBeInTheDocument();
  expect(
    screen.queryByTestId('slicer-readiness-warning'),
  ).not.toBeInTheDocument();
  expect(
    vi
      .mocked(fetch)
      .mock.calls.some(([url]) => String(url).includes('/slicing/readiness')),
  ).toBe(false);
  const details = screen.getByRole('button', {
    name: 'Plate details for Desk kit',
  });
  details.focus();
  await user.keyboard('{Enter}');
  expect(details).toHaveAttribute('aria-expanded', 'true');
  expect(screen.getByText(/6 \+ 6 \+ 6 \+ 2 units/)).toBeInTheDocument();
  expect(
    screen.getByText(/The final plate uses fewer objects/),
  ).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Add run' }));
  await screen.findByText('Saved run');
  expect(posts).toEqual([
    expect.objectContaining({
      lines: [
        expect.objectContaining({
          quantity: 10,
          quantityBasis: 'Sets',
          planVersion: 'kit-Sets-10',
        }),
      ],
    }),
  ]);
});

it('supports keyboard picking and removal, restores focus, and keeps remaining versions correct', async () => {
  const { user, pick, posts } = setup();
  await pick('Desk kit');
  await pick('Nested kit');
  await user.click(
    screen.getByRole('button', { name: 'Add part or assembly' }),
  );
  const search = screen.getByRole('combobox', {
    name: 'Search parts and assemblies',
  });
  await user.type(search, 'Bracket');
  await user.keyboard('{ArrowDown}{Enter}');
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Add run' })).toBeEnabled(),
  );
  const actions = screen.getByRole('button', { name: 'Actions for Desk kit' });
  actions.focus();
  await user.keyboard('{Enter}');
  await user.keyboard('{Enter}');
  await waitFor(() =>
    expect(
      screen.queryByRole('listitem', { name: 'Desk kit' }),
    ).not.toBeInTheDocument(),
  );
  await waitFor(() =>
    expect(
      screen.getByRole('button', { name: 'Add part or assembly' }),
    ).toHaveFocus(),
  );
  await user.click(screen.getByRole('button', { name: 'Add run' }));
  await screen.findByText('Saved run');
  expect(posts).toEqual([
    expect.objectContaining({
      lines: [
        expect.objectContaining({
          targetId: 'nested',
          planVersion: 'nested-Sets-1',
        }),
        expect.objectContaining({
          targetId: 'bracket',
          planVersion: 'bracket-Units-1',
        }),
      ],
    }),
  ]);
});

it('hides incomplete totals, retries preview failures, and retains the draft', async () => {
  let fail = true;
  const { user, pick, summary } = setup({
    preview: (line) =>
      fail ? json({ detail: 'Part changed' }, 409) : json(makePlan(line)),
  });
  await user.type(screen.getByLabelText('Name'), 'Order 42');
  await pick('Desk kit');
  await screen.findByText('Could not plan Desk kit.');
  expect(screen.getByText('Part changed')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Add run' })).toBeDisabled();
  expect(
    summary().queryByText('Printed component units'),
  ).not.toBeInTheDocument();
  fail = false;
  await user.click(screen.getByRole('button', { name: 'Retry' }));
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Add run' })).toBeEnabled(),
  );
  expect(screen.getByLabelText('Name')).toHaveValue('Order 42');
  await user.clear(screen.getByLabelText('Sets'));
  expect(screen.getByRole('button', { name: 'Add run' })).toBeDisabled();
  expect(summary().getByText(/Enter a valid quantity/)).toBeInTheDocument();
});

it('revalidates stale plans and reuses the submission ID after failure', async () => {
  let version = 'before';
  let fail = true;
  const { user, pick, posts } = setup({
    preview: (line) => json({ ...makePlan(line), version }),
    submit: () => {
      if (fail) {
        version = 'after';
        return json({ detail: 'Production inputs changed' }, 409);
      }
      return json({ id: 'new-run' }, 201);
    },
  });
  await pick('Desk kit');
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Add run' })).toBeEnabled(),
  );
  await user.click(screen.getByRole('button', { name: 'Add run' }));
  await waitFor(() => expect(posts).toHaveLength(1));
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Add run' })).toBeEnabled(),
  );
  fail = false;
  await user.click(screen.getByRole('button', { name: 'Add run' }));
  await screen.findByText('Saved run');
  expect(posts[0]).toEqual(
    expect.objectContaining({
      submissionId: expect.any(String),
      lines: [expect.objectContaining({ planVersion: 'before' })],
    }),
  );
  expect(posts[1]).toEqual(
    expect.objectContaining({
      submissionId: (posts[0] as { submissionId: string }).submissionId,
      lines: [expect.objectContaining({ planVersion: 'after' })],
    }),
  );
});

it('locks conflicting controls and repeated submission while adding', async () => {
  let finish!: (response: Response) => void;
  const { user, pick, posts } = setup({
    submit: () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  });
  await pick('Desk kit');
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Add run' })).toBeEnabled(),
  );
  await user.dblClick(screen.getByRole('button', { name: 'Add run' }));
  expect(posts).toHaveLength(1);
  expect(screen.getByRole('button', { name: /Adding…/ })).toBeDisabled();
  expect(
    screen.getByRole('button', { name: 'Add part or assembly' }),
  ).toBeDisabled();
  expect(
    screen.getByRole('button', { name: 'Actions for Desk kit' }),
  ).toBeDisabled();
  expect(screen.getByLabelText('Name')).toBeDisabled();
  expect(screen.getByLabelText('Sets')).toBeDisabled();
  expect(screen.getByRole('combobox', { name: 'Printer' })).toBeDisabled();
  await act(async () => finish(json({ id: 'new-run' }, 201)));
  await screen.findByText('Saved run');
});

it('shows catalog failure and keyboard-accessible Retry', async () => {
  let fail = true;
  const { user } = setup({ catalogFails: () => fail });
  await user.click(
    screen.getByRole('button', { name: 'Add part or assembly' }),
  );
  await screen.findByText('Could not load parts and assemblies.');
  fail = false;
  const retry = screen.getByRole('button', { name: 'Retry' });
  retry.focus();
  await user.keyboard('{Enter}');
  expect(
    await screen.findByRole('option', { name: /Desk kit/ }),
  ).toBeInTheDocument();
});

it('shows partial time and filament independently without treating missing estimates as zero', async () => {
  const { pick, summary } = setup({
    preview: (line) => {
      const plan = makePlan(line);
      plan.parts[0].minutes = 60;
      plan.parts[0].grams = null;
      plan.parts[1].minutes = null;
      plan.parts[1].grams = 0;
      plan.estimate = {
        time: { value: 60, missing: 1 },
        filament: { value: 0, missing: 1 },
        approximate: true,
        colors: [],
      };
      return json(plan);
    },
  });
  await pick('Desk kit');
  expect(await summary().findByText('Partial · ≈ 1h')).toBeInTheDocument();
  expect(summary().getByText('Partial · ≈ 0 g')).toBeInTheDocument();
  expect(summary().queryByText('Unavailable')).not.toBeInTheDocument();
});

it('replaces totals with loading feedback until every current preview resolves', async () => {
  let finish!: (response: Response) => void;
  const { user, pick, summary } = setup({
    preview: (line) =>
      line.quantity === 2
        ? new Promise((resolve) => {
            finish = resolve;
          })
        : json(makePlan(line)),
  });
  await pick('Desk kit');
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Add run' })).toBeEnabled(),
  );
  const sets = screen.getByLabelText('Sets');
  await user.clear(sets);
  await user.type(sets, '2');
  expect(
    screen.getByRole('status', { name: 'Planning Desk kit' }),
  ).toBeInTheDocument();
  expect(
    summary().getByText('Updating production totals…'),
  ).toBeInTheDocument();
  expect(
    summary().queryByText('Printed component units'),
  ).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Add run' })).toBeDisabled();
  await act(async () =>
    finish(
      json(
        makePlan({
          targetId: 'kit',
          targetKind: 'Assembly',
          quantity: 2,
          quantityBasis: 'Sets',
        }),
      ),
    ),
  );
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Add run' })).toBeEnabled(),
  );
  expect(summary().getByText('6')).toBeInTheDocument();
});
