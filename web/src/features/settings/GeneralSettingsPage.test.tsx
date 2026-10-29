import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { MoneyInput } from '@/components/ui/money-input';
import { CostPricingSummary } from '@/features/cost-catalog/CostPricingSummary';
import { formatRate } from '@/features/cost-catalog/costing';
import { formatCost } from '@/lib/formatUnits';
import { GeneralSettingsPage } from './GeneralSettingsPage';
import { CurrencyProvider, useCurrency } from './generalSettings';

const json = (data: unknown, status = 200) =>
  new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
function Prices() {
  const currency = useCurrency();
  return (
    <section>
      <MoneyInput
        aria-label="Example price input"
        type="number"
        defaultValue="12.5"
      />
      <CostPricingSummary
        pricing={{ pricingMode: 'Direct', directRate: 0.00001, unit: 'Piece' }}
      />
      <output aria-label="Example prices">
        {formatCost(12.5, currency)}; {formatRate(0.00001, currency)}
      </output>
    </section>
  );
}
function setup(failLoad = false) {
  let data = {
    currencyCode: 'USD',
    electricityPricePerKwh: 0.5 as number | null,
    supportedCurrencies: ['USD', 'MYR', 'JPY'],
  };
  const writes: unknown[] = [];
  let failSave = false;
  const fetch = vi.fn((_url: string, init?: RequestInit) => {
    if (init?.method === 'PUT') {
      writes.push(JSON.parse(String(init.body)));
      if (failSave) return Promise.resolve(json({}, 500));
      data = { ...data, ...JSON.parse(String(init.body)) };
    } else if (failLoad) return Promise.resolve(json({}, 500));
    return Promise.resolve(json(data));
  });
  vi.stubGlobal('fetch', fetch);
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const router = createMemoryRouter(
    [
      {
        path: '/settings/general',
        element: (
          <CurrencyProvider>
            <GeneralSettingsPage />
            <Prices />
          </CurrencyProvider>
        ),
      },
      { path: '/other', element: <p>Other page</p> },
    ],
    { initialEntries: ['/settings/general'] },
  );
  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return {
    writes,
    fetch,
    client,
    failLoads: () => {
      failLoad = true;
    },
    router,
    setFailSave: (value: boolean) => {
      failSave = value;
    },
    recoverLoad: () => {
      failLoad = false;
    },
  };
}
afterEach(() => vi.unstubAllGlobals());

it('searches and selects currency by keyboard, saves zero, and updates existing displays', async () => {
  const user = userEvent.setup();
  const { writes } = setup();
  const picker = await screen.findByRole('combobox', { name: 'Currency' });
  expect(screen.getByLabelText('Example prices')).toHaveTextContent(
    'USD 12.50; USD 0.00001',
  );
  await user.click(picker);
  await user.type(screen.getByPlaceholderText('Search currencies'), 'ringgit');
  await user.keyboard('{ArrowDown}{Enter}');
  expect(picker).toHaveFocus();
  expect(picker).toHaveTextContent('MYR');
  const price = screen.getByLabelText('Price per kWh');
  await user.clear(price);
  await user.type(price, '0');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() =>
    expect(writes).toEqual([
      {
        currencyCode: 'MYR',
        electricityPricePerKwh: 0,
        failureAllowancePercent: 0,
      },
    ]),
  );
  await waitFor(() =>
    expect(screen.getByLabelText('Example prices')).toHaveTextContent(
      'MYR 12.50; MYR 0.00001',
    ),
  );
  expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  expect(screen.getByText('MYR 0.00001 / piece')).toBeInTheDocument();
  expect(
    screen.getByRole('spinbutton', { name: 'Example price input' }),
  ).toHaveValue(12.5);
});

it('validates the tariff, retains a failed draft, and retries a blank tariff as unknown', async () => {
  const user = userEvent.setup();
  const { writes, setFailSave } = setup();
  const price = await screen.findByLabelText('Price per kWh');
  await user.clear(price);
  await user.type(price, '-1');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(
    await screen.findByText('Enter a nonnegative number.'),
  ).toBeInTheDocument();
  expect(price).toBeInvalid();
  expect(writes).toHaveLength(0);
  await user.clear(price);
  setFailSave(true);
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(
    await screen.findByText('Could not save general settings'),
  ).toBeInTheDocument();
  expect(price).toHaveValue('');
  setFailSave(false);
  await user.click(screen.getByRole('button', { name: 'Retry' }));
  await waitFor(() => expect(writes).toHaveLength(2));
  expect(writes[1]).toEqual({
    currencyCode: 'USD',
    electricityPricePerKwh: null,
    failureAllowancePercent: 0,
  });
});

it('shows a retry instead of rendering prices under an assumed currency on initial load failure', async () => {
  const user = userEvent.setup();
  const { recoverLoad } = setup(true);
  expect(
    await screen.findByText('Could not load general settings'),
  ).toBeInTheDocument();
  expect(screen.queryByLabelText('Example prices')).not.toBeInTheDocument();
  recoverLoad();
  await user.click(screen.getByRole('button', { name: 'Retry' }));
  expect(await screen.findByLabelText('Example prices')).toHaveTextContent(
    'USD 12.50',
  );
});

it('keeps cached currency during a failed background refresh and protects unsaved edits', async () => {
  const user = userEvent.setup();
  const { fetch, router, client, failLoads } = setup();
  const price = await screen.findByLabelText('Price per kWh');
  await user.clear(price);
  await user.type(price, '2');
  failLoads();
  await act(() =>
    client.invalidateQueries({ queryKey: ['/api/v1/settings/general'] }),
  );
  expect(screen.getByLabelText('Example prices')).toHaveTextContent(
    'USD 12.50',
  );
  fetch.mockImplementation(() => Promise.resolve(json({}, 500)));
  await user.click(screen.getByRole('button', { name: 'Save' }));
  await screen.findByText('Could not save general settings');
  expect(screen.getByLabelText('Example prices')).toHaveTextContent(
    'USD 12.50',
  );
  void router.navigate('/other');
  expect(await screen.findByRole('alertdialog')).toHaveTextContent(
    'Discard unsaved changes?',
  );
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  expect(price).toHaveValue('2');
});
