import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PrinterCostsPage } from './PrinterCostsPage';
import { PrinterModelCostPage } from './PrinterModelCostPage';

const model = {
  brand: 'Bambu',
  modelCode: 'P1S',
  canRemove: true,
  printerPurchasePrice: 3000,
  printerUpfrontCost: 500,
  lifetimeServiceCost: 1000,
  lifetimePrintingHours: 5000,
  averagePrintingWatts: 150,
  depreciationPerHour: 0.9,
  electricityPerHour: 0.075,
};
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
function setup(
  path = '/settings/printer-costs',
  fail = false,
  canRemove = true,
) {
  const writes: { url: string; body: Record<string, unknown> }[] = [];
  let models = [{ ...model, canRemove }];
  const fetch = vi.fn((url: string, init?: RequestInit) => {
    if (init?.method === 'DELETE') {
      writes.push({ url, body: {} });
      models = [];
      return Promise.resolve(new Response(null, { status: 204 }));
    }
    if (init?.method === 'PUT') {
      writes.push({ url, body: JSON.parse(String(init.body)) });
      return Promise.resolve(json(model));
    }
    if (url.includes('/printers/catalog'))
      return Promise.resolve(
        json({
          brands: [
            {
              brand: 'Bambu',
              models: [
                { modelCode: 'P1S', name: 'Bambu Lab P1S' },
                { modelCode: 'A1', name: 'Bambu Lab A1' },
              ],
            },
          ],
        }),
      );
    return Promise.resolve(
      fail ? json({}, 500) : json({ electricityPricePerKwh: 0.5, models }),
    );
  });
  vi.stubGlobal('fetch', fetch);
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const router = createMemoryRouter(
    [
      { path: '/settings/printer-costs', element: <PrinterCostsPage /> },
      {
        path: '/settings/printer-costs/new',
        element: <PrinterModelCostPage />,
      },
      {
        path: '/settings/printer-costs/models/:brand/:modelCode',
        element: <PrinterModelCostPage />,
      },
    ],
    { initialEntries: [path] },
  );
  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return { writes, fetch, router };
}
afterEach(() => vi.unstubAllGlobals());
describe('printer costs settings', () => {
  it('opens the trailing row menu by keyboard and navigates to editing', async () => {
    const user = userEvent.setup();
    setup();
    const actions = await screen.findByRole('button', {
      name: 'Actions for Bambu P1S',
    });
    actions.focus();
    await user.keyboard('{Enter}');
    await user.keyboard('{Home}{Enter}');
    expect(
      await screen.findByRole('heading', { name: 'Printer model costs' }),
    ).toBeInTheDocument();
    expect(await screen.findByLabelText('Printer upfront cost')).toHaveValue(
      '500',
    );
  });
  it('saves changed costs and preserves explicit zero versus blank', async () => {
    const user = userEvent.setup();
    const { writes } = setup('/settings/printer-costs/models/Bambu/P1S');
    const upfront = await screen.findByLabelText('Printer upfront cost');
    await waitFor(() => expect(upfront).toHaveValue('500'));
    await user.clear(upfront);
    await user.type(upfront, '0');
    await user.clear(screen.getByLabelText('Lifetime service cost'));
    await user.click(
      screen.getByRole('button', { name: 'Save printer costs' }),
    );
    await waitFor(() =>
      expect(writes[0]?.body).toMatchObject({
        brand: 'Bambu',
        modelCode: 'P1S',
        printerUpfrontCost: 0,
        lifetimeServiceCost: null,
      }),
    );
    expect(
      await screen.findByRole('heading', { name: 'Printer costs' }),
    ).toBeInTheDocument();
  });
  it('rejects zero lifetime hours without sending a request', async () => {
    const user = userEvent.setup();
    const { writes } = setup('/settings/printer-costs/models/Bambu/P1S');
    const hours = await screen.findByLabelText('Lifetime printing hours');
    await waitFor(() => expect(hours).toHaveValue('5000'));
    await user.clear(hours);
    await user.type(hours, '0');
    await user.click(
      screen.getByRole('button', { name: 'Save printer costs' }),
    );
    expect(
      await screen.findByText('Enter positive printing hours.'),
    ).toBeInTheDocument();
    expect(writes).toHaveLength(0);
  });
  it('selects an unused supported model by keyboard and adds it', async () => {
    const user = userEvent.setup();
    const { writes } = setup('/settings/printer-costs/new');
    const trigger = await screen.findByRole('combobox', { name: 'Model' });
    trigger.focus();
    await user.keyboard('{Enter}{ArrowDown}{Enter}');
    await user.click(
      screen.getByRole('button', { name: 'Save printer costs' }),
    );
    await waitFor(() =>
      expect(writes[0]?.body).toMatchObject({
        brand: 'Bambu',
        modelCode: 'A1',
        printerPurchasePrice: null,
      }),
    );
  });
  it('shows load failure with a retry action', async () => {
    const { fetch } = setup('/settings/printer-costs', true);
    expect(
      await screen.findByText('Could not load printer costs'),
    ).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(fetch.mock.calls.length).toBeGreaterThan(2));
  });
  it('links to the shared electricity setting', async () => {
    setup();
    expect(
      await screen.findByRole('link', { name: 'General settings' }),
    ).toHaveAttribute('href', '/settings/general');
    expect(screen.queryByLabelText('Price per kWh')).not.toBeInTheDocument();
  });
});

it('retains edits after a save failure and retries the same values', async () => {
  const user = userEvent.setup();
  const { fetch, writes } = setup('/settings/printer-costs/models/Bambu/P1S');
  const watts = await screen.findByLabelText('Average printing power');
  await waitFor(() => expect(watts).toHaveValue('150'));
  await user.clear(watts);
  await user.type(watts, '200');
  fetch.mockImplementationOnce(() => Promise.resolve(json({}, 500)));
  await user.click(screen.getByRole('button', { name: 'Save printer costs' }));
  expect(
    await screen.findByText('Could not save printer costs'),
  ).toBeInTheDocument();
  expect(watts).toHaveValue('200');
  await user.click(screen.getByRole('button', { name: 'Retry' }));
  await waitFor(() =>
    expect(writes[0]?.body).toMatchObject({ averagePrintingWatts: 200 }),
  );
});

async function openRemoval() {
  const user = userEvent.setup();
  const trigger = await screen.findByRole('button', {
    name: 'Actions for Bambu P1S',
  });
  trigger.focus();
  await user.keyboard('{Enter}{End}{Enter}');
  expect(await screen.findByRole('alertdialog')).toBeInTheDocument();
  return user;
}

it('opens removal with the keyboard, cancels, and restores row action focus', async () => {
  const { writes } = setup();
  const user = await openRemoval();
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  await waitFor(() =>
    expect(
      screen.getByRole('button', { name: 'Actions for Bambu P1S' }),
    ).toHaveFocus(),
  );
  expect(writes).toHaveLength(0);
});

it('confirms removal, refreshes the list, and focuses the add link', async () => {
  const { writes } = setup();
  const user = await openRemoval();
  await user.click(screen.getByRole('button', { name: 'Remove' }));
  await waitFor(() =>
    expect(
      screen.queryByRole('button', { name: 'Actions for Bambu P1S' }),
    ).not.toBeInTheDocument(),
  );
  expect(writes).toEqual([
    { url: '/api/v1/settings/printer-costs/models/Bambu/P1S', body: {} },
  ]);
  expect(screen.getByText('No printer models')).toBeInTheDocument();
  await waitFor(() =>
    expect(
      screen.getByRole('link', { name: 'Add printer model' }),
    ).toHaveFocus(),
  );
});

it('keeps the confirmation open after failure and supports retry', async () => {
  const { fetch, writes } = setup();
  const user = await openRemoval();
  fetch.mockImplementationOnce(() => Promise.resolve(json({}, 500)));
  await user.click(screen.getByRole('button', { name: 'Remove' }));
  expect(
    await screen.findByText('Could not remove printer model costs. Try again.'),
  ).toBeInTheDocument();
  expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Remove' }));
  await waitFor(() => expect(writes).toHaveLength(1));
  await waitFor(() =>
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument(),
  );
});

it('disables repeated removal while the request is pending', async () => {
  const { fetch } = setup();
  const user = await openRemoval();
  let finish!: (response: Response) => void;
  fetch.mockImplementationOnce(
    () =>
      new Promise<Response>((resolve) => {
        finish = resolve;
      }),
  );
  await user.click(screen.getByRole('button', { name: 'Remove' }));
  expect(screen.getByRole('button', { name: 'Remove' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
  await user.click(screen.getByRole('button', { name: 'Remove' }));
  expect(
    fetch.mock.calls.filter(([, init]) => init?.method === 'DELETE'),
  ).toHaveLength(1);
  finish(new Response(null, { status: 204 }));
  await waitFor(() =>
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument(),
  );
});

it('hides Remove for fleet models while keeping Edit costs available', async () => {
  setup('/settings/printer-costs', false, false);
  const user = userEvent.setup();
  const trigger = await screen.findByRole('button', {
    name: 'Actions for Bambu P1S',
  });
  trigger.focus();
  await user.keyboard('{Enter}');
  expect(
    await screen.findByRole('menuitem', { name: 'Edit costs' }),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole('menuitem', { name: 'Remove' }),
  ).not.toBeInTheDocument();
});

it('handles a stale removal when a model has just entered the fleet', async () => {
  const { fetch } = setup();
  const user = await openRemoval();
  fetch.mockImplementationOnce(() =>
    Promise.resolve(
      json({ detail: 'Printer model is still in the fleet' }, 409),
    ),
  );
  const previousFetch = fetch.getMockImplementation();
  if (!previousFetch) throw new Error('Missing fetch implementation.');
  fetch.mockImplementation((url, init) =>
    init?.method === 'DELETE'
      ? Promise.resolve(json({}, 409))
      : url.includes('/settings/printer-costs')
        ? Promise.resolve(
            json({
              electricityPricePerKwh: 0.5,
              models: [{ ...model, canRemove: false }],
            }),
          )
        : previousFetch(url, init),
  );
  await user.click(screen.getByRole('button', { name: 'Remove' }));
  expect(
    await screen.findByText(
      'This model is now used by a printer in the fleet. Remove its printers before removing its cost settings.',
    ),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole('button', { name: 'Remove' }),
  ).not.toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  const trigger = await screen.findByRole('button', {
    name: 'Actions for Bambu P1S',
  });
  trigger.focus();
  await user.keyboard('{Enter}');
  expect(
    await screen.findByRole('menuitem', { name: 'Edit costs' }),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole('menuitem', { name: 'Remove' }),
  ).not.toBeInTheDocument();
});
