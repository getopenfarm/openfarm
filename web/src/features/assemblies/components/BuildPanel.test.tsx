import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  AssemblyQuote,
  QuoteAssemblyRequest,
} from '@/lib/api/generated/endpoints.schemas';
import type { EditableLine } from './BomLineEditor';
import { BuildPanel } from './BuildPanel';
import { PricingSection } from './PricingSection';

// The server computes every total (AssemblyQuoteService, tested in AssemblyQuoteEndpointTests); these tests cover rendering.
function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const QUOTE: AssemblyQuote = {
  parts: 1,
  components: 1,
  print: {
    time: { value: 120, missing: 0 },
    filament: { value: 40, missing: 0 },
    approximate: false,
    colors: [],
  },
  colors: [
    {
      materialType: 'PLA',
      colorHex: '000000',
      variantId: 'v1',
      grams: 40,
      missing: 0,
      name: 'Acme PLA · Black',
      swatchHex: '000000',
      cost: 0.8,
      costPriced: 1,
      costMissing: 0,
    },
  ],
  filament: {
    total: 0.8,
    priced: 1,
    missingSelection: 0,
    missingPrice: 0,
    missingGrams: 0,
    incomplete: false,
  },
  depreciation: { total: 2, priced: 1, missing: 0 },
  electricity: { total: 0.2, priced: 1, missing: 0 },
  failurePercent: 10,
  failure: 0.3,
  failureAvailable: true,
  printPartial: false,
  breakdown: [
    { category: 'Hardware', amount: 1 },
    { category: 'Packaging', amount: 0.5 },
    { category: 'Shipping', amount: 3 },
    { category: 'Labour', amount: 2 },
  ],
  costsPartial: false,
  shipping: 3,
  total: { amount: 9.8, partial: false, available: true },
  notes: [],
  profiles: [
    {
      profileId: 'shop',
      name: 'Shop',
      fees: [{ name: 'Commission', type: 'Percent', amount: 10 }],
      feePercent: 10,
      fixedFee: 0.5,
      pricing: {
        price: 17.17,
        unreachable: false,
        fees: 2.217,
        profit: 5.153,
        margin: 0.3,
        markup: 0.752,
        breakEven: 11.44,
        maxDiscount: 0.333,
      },
    },
  ],
};

const LINES: EditableLine[] = [
  {
    key: 'l1',
    targetKind: 'Part',
    targetId: 'p1',
    targetName: 'Bracket',
    quantity: 2,
  },
  {
    key: 'l2',
    targetKind: 'NonPrintedComponent',
    targetId: 'c1',
    targetName: 'Magnet',
    quantity: 4,
  },
];

let requests: QuoteAssemblyRequest[] = [];
function stubQuote(respond: (body: QuoteAssemblyRequest) => Response) {
  requests = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (!input.toString().includes('/api/v1/assemblies/quote'))
        throw new Error(`Unexpected fetch: ${input}`);
      const body = JSON.parse(String(init?.body)) as QuoteAssemblyRequest;
      requests.push(body);
      return respond(body);
    }),
  );
}

function wrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <MemoryRouter>
        <QueryClientProvider client={queryClient}>
          {children}
        </QueryClientProvider>
      </MemoryRouter>
    );
  };
}

const stat = (label: string) =>
  within(
    screen.getByText(label, { selector: 'dt' }).parentElement as HTMLElement,
  );

describe('BuildPanel', () => {
  beforeEach(() => stubQuote(() => jsonResponse(QUOTE)));
  afterEach(() => vi.unstubAllGlobals());

  it('quotes the draft lines and shows the totals the server returns', async () => {
    render(<BuildPanel lines={LINES} />, { wrapper: wrapper() });

    await waitFor(() =>
      expect(stat('Print time').getByText('2h')).toBeInTheDocument(),
    );
    expect(requests[0].lines).toEqual([
      { targetKind: 'Part', targetId: 'p1', quantity: 2 },
      { targetKind: 'NonPrintedComponent', targetId: 'c1', quantity: 4 },
    ]);
    expect(stat('Filament').getByText('40 g')).toBeInTheDocument();
    const colours = screen.getByRole('table', { name: 'Filament breakdown' });
    expect(within(colours).getByText('Acme PLA · Black')).toBeInTheDocument();
    expect(within(colours).getByText('USD 0.80')).toBeInTheDocument();
    expect(
      stat('Printer depreciation').getByText('USD 2.00'),
    ).toBeInTheDocument();
    expect(
      stat('Failure allowance (10%)').getByText('USD 0.30'),
    ).toBeInTheDocument();
    expect(stat('Hardware cost').getByText('USD 1.00')).toBeInTheDocument();
    expect(stat('Labor cost').getByText('USD 2.00')).toBeInTheDocument();
    expect(stat('Product cost').getByText('USD 6.80')).toBeInTheDocument();
    expect(
      stat('Shipping allowance').getByText('USD 3.00'),
    ).toBeInTheDocument();
    expect(
      stat('Estimated total including shipping').getByText('USD 9.80'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('leaves picker rows without a target out and re-quotes when the lines change', async () => {
    const { rerender } = render(
      <BuildPanel
        lines={[...LINES, { ...LINES[0], key: 'blank', targetId: '' }]}
      />,
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(requests).toHaveLength(1));
    expect(requests[0].lines).toHaveLength(2);

    rerender(<BuildPanel lines={[{ ...LINES[0], quantity: 6 }]} />);

    await waitFor(() => expect(requests).toHaveLength(2));
    expect(requests[1].lines).toEqual([
      { targetKind: 'Part', targetId: 'p1', quantity: 6 },
    ]);
  });

  it('marks partial totals and lists the server notes', async () => {
    stubQuote(() =>
      jsonResponse({
        ...QUOTE,
        costsPartial: true,
        total: { amount: 9.8, partial: true, available: true },
        notes: ['1 component without cost.'],
      }),
    );
    render(<BuildPanel lines={LINES} />, { wrapper: wrapper() });

    expect(
      await screen.findByText('1 component without cost.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Build estimates incomplete')).toBeInTheDocument();
    expect(
      stat('Estimated total including shipping').getByText(
        'USD 9.80 (partial)',
      ),
    ).toBeInTheDocument();
  });

  it('keeps totals unavailable after a failed quote and supports keyboard retry', async () => {
    const user = userEvent.setup();
    let fail = true;
    stubQuote(() => (fail ? jsonResponse({}, 500) : jsonResponse(QUOTE)));
    render(<BuildPanel lines={LINES} />, { wrapper: wrapper() });

    const retry = await screen.findByRole('button', { name: 'Retry totals' });
    expect(
      stat('Estimated total including shipping').getByText('Unavailable'),
    ).toBeInTheDocument();
    fail = false;
    await user.tab();
    expect(retry).toHaveFocus();
    await user.keyboard('{Enter}');

    await waitFor(() =>
      expect(
        stat('Estimated total including shipping').getByText('USD 9.80'),
      ).toBeInTheDocument(),
    );
  });
});

describe('PricingSection', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows each fee profile the server priced, and sends the typed rule', async () => {
    stubQuote(() => jsonResponse(QUOTE));
    render(
      <PricingSection
        lines={LINES}
        additionalCosts={[]}
        value={{ mode: 'Margin', value: '30' }}
        onChange={() => {}}
      />,
      { wrapper: wrapper() },
    );

    const table = await screen.findByRole('table', {
      name: 'Profit by fee profile',
    });
    expect(within(table).getByText('Shop')).toBeInTheDocument();
    expect(within(table).getByText('USD 17.17')).toBeInTheDocument();
    expect(within(table).getByText('USD 5.15')).toBeInTheDocument();
    expect(requests[0].pricing).toEqual({ mode: 'Margin', value: 30 });
    expect(screen.queryByText(/No fee profiles/)).not.toBeInTheDocument();
  });

  it('points to Settings while only the No fees row exists, and names unreachable margins', async () => {
    stubQuote(() =>
      jsonResponse({
        ...QUOTE,
        profiles: [
          {
            profileId: null,
            name: 'No fees',
            fees: [],
            feePercent: 0,
            fixedFee: 0,
            pricing: {
              price: null,
              unreachable: true,
              breakEven: 9.8,
            },
          },
        ],
      }),
    );
    render(
      <PricingSection
        lines={LINES}
        additionalCosts={[]}
        value={{ mode: 'Margin', value: '120' }}
        onChange={() => {}}
      />,
      { wrapper: wrapper() },
    );

    expect(await screen.findByText(/No fee profiles/)).toBeInTheDocument();
    expect(screen.getByText(/No fees fees ≥ 100%/)).toBeInTheDocument();
  });
});
