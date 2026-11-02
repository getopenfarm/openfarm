import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { type ReactNode, useRef, useState } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { productionPlanFixture } from '@/test-utils/productionPlan';
import { AddToRunDialog, SendToQueueDialog } from './SendToQueueDialog';

vi.mock('../hooks/useProductionHub', () => ({
  invalidateProductionQueries: () => {},
}));
const part = { kind: 'Part', id: 'part-1', name: 'Bracket' } as const;
const assembly = { kind: 'Assembly', id: 'lamp', name: 'Lamp' } as const;
function setup(
  node: ReactNode,
  options: { incomplete?: boolean; failOnce?: boolean; delayed?: boolean } = {},
) {
  const posts: Record<string, unknown>[] = [];
  let release: (() => void) | undefined;
  let fail = options.failOnce;
  const fetcher = vi.fn(
    async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (url.includes('/production-demands/preview')) {
        const { line, plateOverrides } = JSON.parse(String(init?.body));
        const plan = productionPlanFixture(
          line.quantity ?? 1,
          line.quantityBasis,
          8,
        );
        if (line.targetKind === 'Assembly' && line.quantityBasis === 'Plates') {
          plan.parts = ['part-1', 'part-2'].map((id, index) => ({
            ...plan.parts[0],
            partId: id,
            name: index ? 'Cover' : 'Bracket',
            unitsPerPlate: index ? 4 : 8,
            plates: plateOverrides?.[id] ?? 1,
            plannedUnits: (plateOverrides?.[id] ?? 1) * (index ? 4 : 8),
          }));
          plan.plates = plan.parts.reduce((sum, p) => sum + p.plates, 0);
          plan.completeSets = Math.floor(
            Math.min(
              plan.parts[0].plannedUnits / 2,
              plan.parts[1].plannedUnits,
            ),
          );
          plan.version = JSON.stringify(plateOverrides);
          if (!plan.plates)
            return Response.json(
              { detail: 'Queue at least one plate.' },
              { status: 422 },
            );
        }
        return Response.json(plan);
      }
      if (init?.method === 'POST') {
        posts.push(JSON.parse(String(init.body)));
        if (fail) {
          fail = false;
          return Response.json({ detail: 'Please retry.' }, { status: 500 });
        }
        if (options.delayed)
          await new Promise<void>((resolve) => {
            release = resolve;
          });
        return Response.json({ id: 'demand-7' });
      }
      return Response.json({
        id: part.id,
        unitsPerPrint: 8,
        printRequirementsStatus: options.incomplete ? 'Incomplete' : 'Complete',
      });
    },
  );
  vi.stubGlobal('fetch', fetcher);
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <MemoryRouter>
      <QueryClientProvider client={client}>{node}</QueryClientProvider>
    </MemoryRouter>,
  );
  return { posts, fetcher, release: () => release?.() };
}
afterEach(() => vi.unstubAllGlobals());
async function send() {
  const button = screen.getByRole('button', { name: 'Print' });
  await waitFor(() => expect(button).toBeEnabled());
  await userEvent.click(button);
}
it('queues full plates without unit or run controls', async () => {
  const { posts, fetcher } = setup(
    <SendToQueueDialog target={part} onClose={() => {}} />,
  );
  const count = await screen.findByLabelText('Number of plates');
  expect(count).toHaveValue(1);
  expect(screen.queryByText('Unit target')).not.toBeInTheDocument();
  expect(screen.queryByText('Track in a Run')).not.toBeInTheDocument();
  expect(
    screen.getByRole('switch', { name: 'Continuous backfill' }),
  ).not.toBeChecked();
  await userEvent.clear(count);
  await userEvent.type(count, '3');
  expect(
    await screen.findByText(/3 plates · 24 planned units/),
  ).toBeInTheDocument();
  expect(screen.queryByRole('table')).not.toBeInTheDocument();
  await send();
  expect(posts[0]).toMatchObject({
    line: { quantity: 3, quantityBasis: 'Plates' },
  });
  expect(posts[0]).not.toHaveProperty('productionRunId');
  expect(posts[0]).not.toHaveProperty('createRun');
  expect(
    fetcher.mock.calls.some(([url]) =>
      String(url).includes('/production-runs'),
    ),
  ).toBe(false);
});
it('edits component plates with stable keyboard focus and permits omission', async () => {
  const { posts } = setup(
    <SendToQueueDialog target={assembly} onClose={() => {}} />,
  );
  const bracket = await screen.findByLabelText('Plates for Bracket');
  expect(bracket).toHaveValue(1);
  const user = userEvent.setup();
  await user.click(bracket);
  await user.clear(bracket);
  await user.type(bracket, '12');
  expect(bracket).toHaveFocus();
  expect(bracket).toHaveValue(12);
  const cover = screen.getByLabelText('Plates for Cover');
  await user.clear(cover);
  await user.type(cover, '0');
  await send();
  expect(posts[0]).toMatchObject({
    line: { targetKind: 'Assembly', quantity: 1, quantityBasis: 'Plates' },
    plateOverrides: { 'part-1': 12, 'part-2': 0 },
  });
});
it('keeps inputs available after all-zero plans fail and validates component counts', async () => {
  setup(<SendToQueueDialog target={assembly} onClose={() => {}} />);
  const bracket = await screen.findByLabelText('Plates for Bracket');
  for (const value of ['', '-1', '1.5']) {
    fireEvent.change(bracket, { target: { value } });
    expect(bracket).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('button', { name: 'Print' })).toBeDisabled();
  }
  fireEvent.change(bracket, { target: { value: '0' } });
  fireEvent.change(screen.getByLabelText('Plates for Cover'), {
    target: { value: '0' },
  });
  expect(
    await screen.findByText('Choose at least one plate.'),
  ).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Print' })).toBeDisabled();
  fireEvent.change(screen.getByLabelText('Plates for Cover'), {
    target: { value: '2' },
  });
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Print' })).toBeEnabled(),
  );
});
it('switches backfill with Space, restores finite counts and submits the whole recipe', async () => {
  const { posts } = setup(
    <SendToQueueDialog target={assembly} onClose={() => {}} />,
  );
  fireEvent.change(await screen.findByLabelText('Plates for Bracket'), {
    target: { value: '3' },
  });
  const toggle = screen.getByRole('switch', { name: 'Continuous backfill' });
  toggle.focus();
  await userEvent.keyboard(' ');
  expect(toggle).toBeChecked();
  expect(posts).toHaveLength(0);
  expect(screen.queryByRole('table')).not.toBeInTheDocument();
  expect(
    screen.getByText(/Uses the whole assembly recipe/),
  ).toBeInTheDocument();
  await userEvent.keyboard(' ');
  expect(await screen.findByLabelText('Plates for Bracket')).toHaveValue(3);
  await userEvent.click(toggle);
  const button = screen.getByRole('button', { name: 'Enable backfill' });
  await waitFor(() => expect(button).toBeEnabled());
  await userEvent.click(button);
  expect(posts[0]).toMatchObject({
    line: { quantity: null, quantityBasis: 'Sets' },
  });
  expect(posts[0]).not.toHaveProperty('plateOverrides');
});
it('defaults run parts to units with fixed ownership and retains explicit plates', async () => {
  const { posts } = setup(
    <AddToRunDialog runId="run-7" target={part} onClose={() => {}} />,
  );
  const input = await screen.findByLabelText('Quantity (units)');
  expect(input).toHaveValue(1);
  expect(screen.queryByRole('switch')).not.toBeInTheDocument();
  fireEvent.change(input, { target: { value: '10' } });
  const button = screen.getByRole('button', { name: 'Add to run' });
  await waitFor(() => expect(button).toBeEnabled());
  await userEvent.click(button);
  expect(posts[0]).toMatchObject({
    productionRunId: 'run-7',
    line: { quantity: 10, quantityBasis: 'Units' },
  });
  expect(screen.getByRole('radio', { name: 'Plates' })).toBeInTheDocument();
});
it('keeps assembly run targets in sets', async () => {
  const { posts } = setup(
    <AddToRunDialog runId="run-7" target={assembly} onClose={() => {}} />,
  );
  expect(await screen.findByLabelText('Quantity (sets)')).toHaveValue(1);
  const button = screen.getByRole('button', { name: 'Add to run' });
  await waitFor(() => expect(button).toBeEnabled());
  await userEvent.click(button);
  expect(posts[0]).toMatchObject({
    productionRunId: 'run-7',
    line: { quantityBasis: 'Sets' },
  });
});
it('blocks incomplete parts', async () => {
  setup(<SendToQueueDialog target={part} onClose={() => {}} />, {
    incomplete: true,
  });
  expect(
    await screen.findByText(/Print requirements incomplete/),
  ).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Print' })).toBeDisabled();
});
it('reuses submission IDs on retry and disables conflicting controls while pending', async () => {
  const onClose = vi.fn();
  const { posts, release } = setup(
    <SendToQueueDialog target={part} onClose={onClose} />,
    { failOnce: true, delayed: true },
  );
  await send();
  expect(await screen.findByText('Please retry.')).toBeInTheDocument();
  await send();
  expect(
    await screen.findByRole('button', { name: /Sending…/ }),
  ).toBeDisabled();
  expect(screen.getByRole('switch')).toBeDisabled();
  expect(screen.getByLabelText('Number of plates')).toBeDisabled();
  await userEvent.keyboard('{Escape}');
  expect(onClose).not.toHaveBeenCalled();
  expect(posts[1].submissionId).toBe(posts[0].submissionId);
  release();
  await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
});
it('restores trigger focus on keyboard dismissal', async () => {
  function Example() {
    const ref = useRef<HTMLButtonElement>(null);
    const [open, setOpen] = useState(true);
    return (
      <>
        <button type="button" ref={ref}>
          Queue Bracket
        </button>
        {open && (
          <SendToQueueDialog
            target={part}
            returnFocusRef={ref}
            onClose={() => setOpen(false)}
          />
        )}
      </>
    );
  }
  setup(<Example />);
  await screen.findByLabelText('Number of plates');
  await userEvent.keyboard('{Escape}');
  await waitFor(() =>
    expect(screen.getByRole('button', { name: 'Queue Bracket' })).toHaveFocus(),
  );
});
