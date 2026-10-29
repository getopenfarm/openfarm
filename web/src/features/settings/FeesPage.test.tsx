import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { FeeProfileDto } from '@/lib/api/generated/endpoints.schemas';
import { FeeProfilePage } from './FeeProfilePage';
import { FeesPage } from './FeesPage';
import { feesKey } from './fees';

const initial: FeeProfileDto[] = [
  { id: 'direct', name: 'Direct', fees: [] },
  {
    id: 'shop',
    name: 'Shop',
    fees: [
      { name: 'Commission', type: 'Percent', amount: 10 },
      { name: 'Listing', type: 'Fixed', amount: 0.5 },
    ],
  },
];
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
function setup(path = '/settings/fees', rows = initial, failRead = false) {
  let profiles = structuredClone(rows);
  const state = { failRead, failWrite: false };
  const writes: { url: string; method: string; body?: FeeProfileDto }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET';
      if (method === 'GET')
        return state.failRead ? json({}, 500) : json({ profiles });
      const body = init?.body ? JSON.parse(String(init.body)) : undefined;
      writes.push({ url, method, body });
      if (state.failWrite) return json({}, 500);
      const id = url.split('/').at(-1);
      if (method === 'DELETE') {
        profiles = profiles.filter((p) => p.id !== id);
        return new Response(null, { status: 204 });
      }
      const saved = { ...body, id: method === 'POST' ? 'created' : id };
      profiles = [...profiles.filter((p) => p.id !== saved.id), saved];
      return json(saved, method === 'POST' ? 201 : 200);
    }),
  );
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const router = createMemoryRouter(
    [
      { path: '/settings/fees', element: <FeesPage /> },
      { path: '/settings/fees/new', element: <FeeProfilePage /> },
      { path: '/settings/fees/:id', element: <FeeProfilePage /> },
    ],
    { initialEntries: [path] },
  );
  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return { state, writes, router, client };
}
afterEach(() => vi.unstubAllGlobals());

describe('fee profile settings', () => {
  it('shows overview totals and real links without a Save footer', async () => {
    setup();
    expect(await screen.findByRole('link', { name: 'Shop' })).toHaveAttribute(
      'href',
      '/settings/fees/shop',
    );
    expect(
      screen.getByText('10% of sale price + USD 0.50 per order'),
    ).toBeInTheDocument();
    expect(screen.getByText('2 fees')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Save' }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });
  it('opens the row menu and editor by keyboard', async () => {
    const user = userEvent.setup();
    setup();
    (await screen.findByRole('button', { name: 'Actions for Shop' })).focus();
    await user.keyboard('{Enter}{Home}{Enter}');
    expect(
      await screen.findByRole('textbox', { name: 'Profile name' }),
    ).toHaveValue('Shop');
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });
  it('saves only the selected profile and returns without a discard prompt', async () => {
    const user = userEvent.setup();
    const { writes, client } = setup('/settings/fees/shop');
    const amount = (
      await screen.findAllByLabelText('Amount', { exact: true })
    )[0];
    await user.clear(amount);
    await user.type(amount, '20');
    await user.click(screen.getByRole('button', { name: 'Try a sale amount' }));
    expect(screen.getByText('USD 20.50')).toBeInTheDocument();
    expect(screen.getByText('USD 79.50')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByRole('link', { name: 'Shop' });
    expect(writes).toHaveLength(1);
    expect(writes[0]).toMatchObject({
      method: 'PUT',
      url: `${feesKey}/profiles/shop`,
      body: { name: 'Shop' },
    });
    expect(
      client
        .getQueryData<{ data: { profiles: FeeProfileDto[] } }>([feesKey])
        ?.data.profiles.find((p) => p.id === 'direct'),
    ).toEqual(initial[0]);
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });
  it('creates a fee-free profile', async () => {
    const user = userEvent.setup();
    const { writes } = setup('/settings/fees/new');
    await user.type(
      await screen.findByRole('textbox', { name: 'Profile name' }),
      'Cash',
    );
    await user.click(screen.getByRole('button', { name: 'Try a sale amount' }));
    expect(screen.getByText('USD 0.00')).toBeInTheDocument();
    expect(screen.getByText('USD 100.00')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByRole('link', { name: 'Cash' });
    expect(writes[0]).toMatchObject({
      method: 'POST',
      url: `${feesKey}/profiles`,
      body: { name: 'Cash', fees: [] },
    });
  });
  it('adds rows, supports keyboard type selection, and restores focus after removal', async () => {
    const user = userEvent.setup();
    setup('/settings/fees/new');
    await user.click(await screen.findByRole('button', { name: 'Add fee' }));
    const name = screen.getByRole('textbox', { name: 'Fee name' });
    expect(name).toHaveFocus();
    await user.type(name, 'Listing');
    screen.getByRole('combobox').focus();
    await user.keyboard('{Enter}{End}{Enter}');
    expect(screen.getByRole('combobox')).toHaveTextContent('Fixed per order');
    await user.type(screen.getByLabelText('Amount', { exact: true }), '0.5');
    expect(screen.getByText('USD 0.50 per order')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Remove Listing' }));
    expect(screen.getByRole('button', { name: 'Add fee' })).toHaveFocus();
  });
  it('rejects duplicate names and combined percentage totals of 100', async () => {
    const user = userEvent.setup();
    const { writes } = setup('/settings/fees/shop');
    const name = await screen.findByRole('textbox', {
      name: 'Profile name',
    });
    await user.clear(name);
    await user.type(name, ' direct ');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(
      await screen.findByText('Fee profile names must be unique.'),
    ).toBeInTheDocument();
    await user.clear(name);
    await user.type(name, 'Shop');
    const amounts = screen.getAllByLabelText('Amount', { exact: true });
    await user.clear(amounts[0]);
    await user.type(amounts[0], '60');
    screen.getAllByRole('combobox')[1].focus();
    await user.keyboard('{Enter}{Home}{Enter}');
    await user.clear(amounts[1]);
    await user.type(amounts[1], '40');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(
      await screen.findByText('Percent fees must total below 100%.'),
    ).toBeInTheDocument();
    expect(writes).toHaveLength(0);
  });
  it('associates invalid amount feedback and suppresses invalid calculations', async () => {
    const user = userEvent.setup();
    const { writes } = setup('/settings/fees/shop');
    const amount = (
      await screen.findAllByLabelText('Amount', { exact: true })
    )[0];
    await user.click(screen.getByRole('button', { name: 'Try a sale amount' }));
    await user.clear(amount);
    await user.type(amount, 'Infinity');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(amount).toHaveAttribute('aria-invalid', 'true');
    expect(amount).toHaveAccessibleDescription(
      'Enter a finite number of 0 or more.',
    );
    expect(screen.queryByText('USD 89.50')).not.toBeInTheDocument();
    expect(writes).toHaveLength(0);
  });
  it('retains failed-save drafts and retries', async () => {
    const user = userEvent.setup();
    const { state } = setup('/settings/fees/shop');
    await user.type(
      await screen.findByRole('textbox', { name: 'Profile name' }),
      ' revised',
    );
    state.failWrite = true;
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(
      await screen.findByText('Could not save fee profile'),
    ).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Profile name' })).toHaveValue(
      'Shop revised',
    );
    state.failWrite = false;
    await user.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() =>
      expect(
        screen.getByRole('link', { name: 'Shop revised' }),
      ).toBeInTheDocument(),
    );
  });
  it('protects unsaved Cancel navigation and allows explicit discard', async () => {
    const user = userEvent.setup();
    const { writes } = setup('/settings/fees/shop');
    await user.type(
      await screen.findByRole('textbox', { name: 'Profile name' }),
      ' draft',
    );
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    const dialog = await screen.findByRole('alertdialog');
    expect(
      within(dialog).getByText('Discard unsaved changes?'),
    ).toBeInTheDocument();
    await user.click(
      within(dialog).getByRole('button', { name: 'Discard changes' }),
    );
    expect(
      await screen.findByRole('link', { name: 'Shop' }),
    ).toBeInTheDocument();
    expect(writes).toHaveLength(0);
  });
  it('confirms removal, restores focus on cancel, and supports failed removal retry', async () => {
    const user = userEvent.setup();
    const { state } = setup();
    await screen.findByRole('button', {
      name: 'Actions for Shop',
    });
    screen.getByRole('button', { name: 'Actions for Shop' }).focus();
    await user.keyboard('{Enter}{End}{Enter}');
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', {
        name: 'Cancel',
      }),
    );
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: 'Actions for Shop' }),
      ).toHaveFocus(),
    );
    screen.getByRole('button', { name: 'Actions for Shop' }).focus();
    await user.keyboard('{Enter}{End}{Enter}');
    state.failWrite = true;
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', {
        name: 'Remove',
      }),
    );
    expect(
      await screen.findByText('Could not remove fee profile. Try again.'),
    ).toBeInTheDocument();
    state.failWrite = false;
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', {
        name: 'Remove',
      }),
    );
    await waitFor(() =>
      expect(
        screen.queryByRole('link', { name: 'Shop' }),
      ).not.toBeInTheDocument(),
    );
    await waitFor(() =>
      expect(
        screen.getByRole('link', { name: 'Add fee profile' }),
      ).toHaveFocus(),
    );
    expect(screen.getByRole('link', { name: 'Direct' })).toBeInTheDocument();
  });
  it('shows missing-profile and failed-load states with recovery', async () => {
    const user = userEvent.setup();
    const { state } = setup('/settings/fees/missing', initial, true);
    expect(
      await screen.findByText('Could not load fee profile'),
    ).toBeInTheDocument();
    state.failRead = false;
    await user.click(screen.getByRole('button', { name: 'Retry' }));
    expect(
      await screen.findByText('Fee profile not found'),
    ).toBeInTheDocument();
  });
  it('keeps example changes outside the saved draft and recalculates zero sales', async () => {
    const user = userEvent.setup();
    setup('/settings/fees/shop');
    await user.click(
      await screen.findByRole('button', { name: 'Try a sale amount' }),
    );
    const sale = screen.getByLabelText('Sale amount');
    await user.clear(sale);
    await user.type(sale, '0');
    expect(screen.getByText('USD 0.50')).toBeInTheDocument();
    expect(screen.getByText('USD -0.50')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    await user.clear(sale);
    expect(sale).toHaveAccessibleDescription(
      'Enter a finite number of 0 or more.',
    );
    expect(screen.getAllByText('—')).toHaveLength(2);
  });
  it('disables conflicting controls while saving and prevents repeat requests', async () => {
    const user = userEvent.setup();
    const { writes } = setup('/settings/fees/shop');
    const name = await screen.findByRole('textbox', {
      name: 'Profile name',
    });
    await user.type(name, ' revised');
    const originalFetch = globalThis.fetch;
    let finish!: () => void;
    const gate = new Promise<void>((resolve) => {
      finish = resolve;
    });
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init?: RequestInit) => {
        if (init?.method === 'PUT') await gate;
        return originalFetch(url, init);
      }),
    );
    await user.click(screen.getByRole('button', { name: 'Save' }));
    const saving = await screen.findByRole('button', { name: 'Saving…' });
    expect(saving).toBeDisabled();
    expect(name).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Remove' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Add fee' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    await user.click(saving);
    finish();
    await screen.findByRole('link', { name: 'Shop revised' });
    expect(writes).toHaveLength(1);
  });
  it('removes from the editor without a second discard prompt', async () => {
    const user = userEvent.setup();
    const { writes } = setup('/settings/fees/shop');
    await user.type(
      await screen.findByRole('textbox', { name: 'Profile name' }),
      ' draft',
    );
    await user.click(screen.getByRole('button', { name: 'Remove' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', {
        name: 'Remove',
      }),
    );
    await waitFor(() =>
      expect(screen.getByRole('link', { name: 'Direct' })).toBeInTheDocument(),
    );
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(writes).toEqual([
      { url: `${feesKey}/profiles/shop`, method: 'DELETE', body: undefined },
    ]);
  });
  it('keeps optional help collapsed and supports keyboard disclosure without changing the draft', async () => {
    const user = userEvent.setup();
    setup('/settings/fees/new');
    const example = await screen.findByRole('button', {
      name: 'Try a sale amount',
    });
    const monthly = screen.getByRole('button', {
      name: 'Include a monthly fee',
    });
    expect(example).toHaveAttribute('aria-expanded', 'false');
    expect(monthly).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByLabelText('Sale amount')).not.toBeInTheDocument();
    expect(
      screen.queryByText(/Divide the monthly fee/),
    ).not.toBeInTheDocument();
    expect(
      screen.getByText('No fees added. This profile has no sale fees.'),
    ).toBeInTheDocument();
    example.focus();
    await user.keyboard('{Enter}');
    expect(example).toHaveAttribute('aria-expanded', 'true');
    const amount = screen.getByLabelText('Sale amount');
    await user.clear(amount);
    await user.type(amount, '50');
    example.focus();
    await user.keyboard(' ');
    expect(example).toHaveAttribute('aria-expanded', 'false');
    expect(example).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(screen.getByLabelText('Sale amount')).toHaveValue('50');
    monthly.focus();
    await user.keyboard('{Enter}');
    expect(screen.getByText(/Divide the monthly fee/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });
  it('paginates complete collections and resets page when size changes', async () => {
    const user = userEvent.setup();
    setup(
      '/settings/fees',
      Array.from({ length: 30 }, (_, index) => ({
        id: String(index),
        name: `Profile ${index + 1}`,
        fees: [],
      })),
    );
    await screen.findByRole('link', { name: 'Profile 1' });
    expect(
      screen.queryByRole('link', { name: 'Profile 30' }),
    ).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Next table page' }));
    expect(
      screen.getByRole('link', { name: 'Profile 30' }),
    ).toBeInTheDocument();
    screen.getByRole('combobox').focus();
    await user.keyboard('{Enter}{End}{Enter}');
    expect(screen.getByRole('link', { name: 'Profile 1' })).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Profile 30' }),
    ).toBeInTheDocument();
  });
});
