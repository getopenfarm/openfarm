import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EditableLine } from './BomLineEditor';
import { BuildPanel } from './BuildPanel';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const ROUTES: [string, unknown][] = [
  ['/api/v1/filaments', { filaments: [] }],
  [
    '/api/v1/assemblies/a1/explosion',
    {
      printedParts: [{ partId: 'p1', partName: 'Bracket', quantity: 1 }],
      nonPrintedComponents: [
        {
          nonPrintedComponentId: 'c2',
          name: 'M3 screw',
          quantity: 4,
        },
      ],
    },
  ],
  [
    '/api/v1/parts',
    {
      parts: [
        {
          id: 'p1',
          name: 'Bracket',
          unitsPerPrint: 2,
          estimatedPrintTimeMinutes: 60,
          estimatedFilamentWeightGrams: 10,
        },
      ],
      totalCount: 1,
    },
  ],
  ['/api/v1/assemblies', { items: [] }],
  [
    '/api/v1/cost-items',
    {
      items: [
        {
          id: 'c1',
          name: 'Magnet',
          effectiveRate: 0.25,
          pricing: {
            itemType: 'Physical',
            unit: 'Piece',
            category: 'Hardware',
          },
        },
        {
          id: 'c2',
          name: 'M3 screw',
          effectiveRate: 0.05,
          pricing: {
            itemType: 'Physical',
            unit: 'Piece',
            category: 'Hardware',
          },
        },
      ],
    },
  ],
];

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
    quantity: 2,
  },
  {
    key: 'l3',
    targetKind: 'Assembly',
    targetId: 'a1',
    targetName: 'Hinge',
    quantity: 2,
  },
];

function renderPanel(lines: EditableLine[]) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }
  return render(<BuildPanel lines={lines} />, { wrapper: Wrapper });
}

const stat = (label: string) =>
  within(
    screen.getByText(label, { selector: 'dt' }).parentElement as HTMLElement,
  );

describe('BuildPanel', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        const hit = ROUTES.find(([path]) => url.includes(path));
        return hit
          ? Promise.resolve(jsonResponse(hit[1]))
          : Promise.reject(new Error(`Unexpected fetch: ${url}`));
      }),
    );
  });
  afterEach(() => vi.unstubAllGlobals());

  it('rolls up the draft lines into totals only, without a save', async () => {
    renderPanel(LINES);

    // Bracket: 2 direct + 2 x 1 via Hinge = 4 units / 2 per plate x 60m.
    await vi.waitFor(() =>
      expect(stat('Print time').getByText('2h')).toBeTruthy(),
    );
    expect(stat('Filament').getByText('20 g')).toBeInTheDocument();
    // 2 x 0.25 + 8 x 0.05
    expect(stat('Hardware cost').getByText('0.90')).toBeInTheDocument();
    // Totals only — no per-line BOM rows.
    expect(screen.queryByText('Bracket')).not.toBeInTheDocument();
    expect(screen.queryByText('M3 screw')).not.toBeInTheDocument();
  });

  it('lists filament grams by colour, adding up to the total', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        if (url.includes('/api/v1/parts'))
          return Promise.resolve(
            jsonResponse({
              parts: [
                {
                  id: 'p1',
                  name: 'Bracket',
                  unitsPerPrint: 1,
                  estimatedPrintTimeMinutes: 60,
                  estimatedFilamentWeightGrams: 10,
                  filamentByColor: [
                    { materialType: 'PLA', colorHex: '#000000', grams: 7.4 },
                    { materialType: 'PLA', colorHex: '#FFFFFF', grams: 3.1 },
                  ],
                },
              ],
            }),
          );
        return Promise.resolve(jsonResponse({ items: [] }));
      }),
    );
    renderPanel(LINES.slice(0, 1));

    const colours = await screen.findByRole('table', {
      name: 'Filament breakdown',
    });
    // 2 units x (7.4 + 3.1): purge-inclusive, not the slicer's 10 g.
    expect(within(colours).getByText('PLA #000000')).toBeInTheDocument();
    expect(within(colours).getByText('14.8 g')).toBeInTheDocument();
    expect(within(colours).getByText('6.2 g')).toBeInTheDocument();
    expect(stat('Filament').getByText('21 g')).toBeInTheDocument();
  });

  it('updates when the lines prop changes', async () => {
    const { rerender } = renderPanel(LINES.slice(0, 1));
    await vi.waitFor(() =>
      expect(stat('Print time').getByText('1h')).toBeTruthy(),
    );

    rerender(<BuildPanel lines={[{ ...LINES[0], quantity: 6 }]} />);
    expect(stat('Print time').getByText('3h')).toBeInTheDocument();
  });

  it('shows unattributed weight even when no known colors are available', async () => {
    renderPanel(LINES.slice(0, 1));
    const table = await screen.findByRole('table', {
      name: 'Filament breakdown',
    });
    const row = within(table).getByRole('row', { name: /Unknown color/ });
    expect(within(row).getByRole('cell', { name: '10 g' })).toBeInTheDocument();
    expect(
      within(row).getByRole('cell', { name: 'Unavailable' }),
    ).toBeInTheDocument();
  });

  it.each([
    { price: 0, rowCost: '0.00', subtotal: '0.00' },
    { price: null, rowCost: 'Unavailable', subtotal: '3.00 (partial)' },
  ])(
    'keeps zero and missing prices distinct in the build sheet ($price)',
    async ({ price, rowCost, subtotal }) => {
      vi.stubGlobal(
        'fetch',
        vi.fn((input: RequestInfo | URL) => {
          const url = input.toString();
          if (url.includes('/api/v1/filaments'))
            return Promise.resolve(
              jsonResponse({
                filaments: [
                  {
                    variants: [
                      { id: 'black', pricePerKg: 30 },
                      { id: 'white', colorName: 'White', pricePerKg: price },
                    ],
                  },
                ],
              }),
            );
          if (url.includes('/api/v1/parts'))
            return Promise.resolve(
              jsonResponse({
                parts: [
                  {
                    id: 'p1',
                    unitsPerPrint: 2,
                    estimatedPrintTimeMinutes: 60,
                    filamentByColor: [
                      {
                        materialType: 'PLA',
                        colorHex: '#000000',
                        grams: 100,
                        narrowFilamentVariantId: 'black',
                      },
                      {
                        materialType: 'PLA',
                        colorHex: '#FFFFFF',
                        grams: 50,
                        narrowFilamentVariantId: 'white',
                      },
                    ],
                  },
                ],
              }),
            );
          const hit = ROUTES.find(([path]) => url.includes(path));
          return Promise.resolve(jsonResponse(hit?.[1] ?? {}));
        }),
      );
      renderPanel(LINES.slice(0, 1));
      const table = await screen.findByRole('table', {
        name: 'Filament breakdown',
      });
      const row = within(table).getByRole('row', { name: /White/ });
      expect(
        within(row).getByRole('cell', { name: '50 g' }),
      ).toBeInTheDocument();
      expect(
        within(row).getByRole('cell', { name: rowCost }),
      ).toBeInTheDocument();
      expect(
        stat('Filament cost').getByText(price === 0 ? '3.00' : subtotal),
      ).toBeInTheDocument();
      expect(
        stat('Estimated total including shipping').getByText(
          price === 0 ? '3.00' : subtotal,
        ),
      ).toBeInTheDocument();
    },
  );

  it('flags parts without an estimate and components without a cost', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        if (url.includes('/api/v1/parts'))
          return Promise.resolve(
            jsonResponse({ parts: [{ id: 'p1', name: 'Bracket' }] }),
          );
        if (url.includes('/api/v1/cost-items'))
          return Promise.resolve(
            jsonResponse({ items: [{ id: 'c1', name: 'Magnet' }] }),
          );
        return Promise.resolve(jsonResponse({ items: [] }));
      }),
    );
    renderPanel(LINES.slice(0, 2));
    expect(
      await screen.findByText('1 part not estimated.'),
    ).toBeInTheDocument();
    expect(screen.getByText('1 component without cost.')).toBeInTheDocument();
  });

  it('shows catalog names and keeps same-color variants and unselected material separate', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        if (url.includes('/api/v1/filaments'))
          return Promise.resolve(
            jsonResponse({
              filaments: [
                {
                  vendorName: 'Bambu Lab',
                  material: 'PETG Basic',
                  pricePerKg: 20,
                  variants: [
                    { id: 'basic', colorName: 'White', colorHex: '#FFFFFF' },
                  ],
                },
                {
                  vendorName: 'Polymaker',
                  material: 'PETG',
                  pricePerKg: 30,
                  variants: [
                    {
                      id: 'premium',
                      colorName: 'Snow white',
                      colorHex: '#FFFFFF',
                    },
                  ],
                },
              ],
            }),
          );
        if (url.includes('/api/v1/parts'))
          return Promise.resolve(
            jsonResponse({
              parts: [
                {
                  id: 'p1',
                  unitsPerPrint: 2,
                  estimatedPrintTimeMinutes: 60,
                  filamentByColor: [
                    {
                      materialType: 'PETG',
                      colorHex: '#FFFFFF',
                      grams: 100,
                      narrowFilamentVariantId: 'basic',
                    },
                    {
                      materialType: 'PETG',
                      colorHex: '#FFFFFF',
                      grams: 50,
                      narrowFilamentVariantId: 'premium',
                    },
                    { materialType: 'PETG', colorHex: '#FFFFFF', grams: 10 },
                    {
                      materialType: 'PETG',
                      colorHex: '#FFFFFF',
                      grams: 5,
                      narrowFilamentVariantId: 'removed',
                    },
                  ],
                },
              ],
            }),
          );
        const hit = ROUTES.find(([path]) => url.includes(path));
        return Promise.resolve(jsonResponse(hit?.[1] ?? {}));
      }),
    );
    renderPanel(LINES.slice(0, 1));
    const table = await screen.findByRole('table', {
      name: 'Filament breakdown',
    });
    const basic = within(table).getByRole('row', {
      name: /Bambu Lab PETG Basic · White/,
    });
    expect(
      within(basic).getByRole('cell', { name: '100 g' }),
    ).toBeInTheDocument();
    expect(
      within(basic).getByRole('cell', { name: '2.00' }),
    ).toBeInTheDocument();
    const premium = within(table).getByRole('row', {
      name: /Polymaker PETG · Snow white/,
    });
    expect(
      within(premium).getByRole('cell', { name: '50 g' }),
    ).toBeInTheDocument();
    expect(
      within(premium).getByRole('cell', { name: '1.50' }),
    ).toBeInTheDocument();
    expect(
      within(table).getByRole('row', { name: /PETG #FFFFFF/ }),
    ).toHaveTextContent('10 g');
    expect(
      within(table).getByRole('row', { name: /Filament unavailable/ }),
    ).toHaveTextContent('5 g');
    expect(stat('Filament').getByText('165 g')).toBeInTheDocument();
    expect(
      stat('Filament cost').getByText('3.50 (partial)'),
    ).toBeInTheDocument();
  });

  it('marks totals as loading while a sub-assembly explodes', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise(() => {})),
    );
    renderPanel(LINES.slice(2, 3));
    expect(screen.getByText('Loading sub-assemblies…')).toBeInTheDocument();
    expect(
      screen.getByRole('complementary', { name: 'Build' }),
    ).toHaveAttribute('aria-busy', 'true');
    expect(
      screen.getByLabelText('Loading filament breakdown'),
    ).toBeInTheDocument();
    expect(
      stat('Estimated total including shipping').queryByText('0.00'),
    ).not.toBeInTheDocument();
  });

  it('marks totals incomplete when a sub-assembly explosion fails', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        if (url.includes('/explosion'))
          return Promise.resolve(jsonResponse({}, 500));
        const hit = ROUTES.find(([path]) => url.includes(path));
        return Promise.resolve(jsonResponse(hit?.[1] ?? {}));
      }),
    );
    renderPanel(LINES);
    expect(
      await screen.findByText(
        'Could not load 1 sub-assembly. Totals incomplete.',
      ),
    ).toBeInTheDocument();
  });
  it('shows selected filament costs, color breakdown and combined hardware total', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        if (url.includes('/api/v1/filaments'))
          return Promise.resolve(
            jsonResponse({
              filaments: [
                {
                  pricePerKg: 20,
                  variants: [{ id: 'black', pricePerKg: 30 }, { id: 'white' }],
                },
              ],
            }),
          );
        if (url.includes('/api/v1/parts'))
          return Promise.resolve(
            jsonResponse({
              parts: [
                {
                  id: 'p1',
                  unitsPerPrint: 2,
                  estimatedPrintTimeMinutes: 60,
                  filamentByColor: [
                    {
                      materialType: 'PLA',
                      colorHex: '#000000',
                      grams: 100,
                      narrowFilamentVariantId: 'black',
                    },
                    {
                      materialType: 'PLA',
                      colorHex: '#FFFFFF',
                      grams: 50,
                      narrowFilamentVariantId: 'white',
                    },
                  ],
                },
              ],
            }),
          );
        const hit = ROUTES.find(([path]) => url.includes(path));
        return Promise.resolve(jsonResponse(hit?.[1] ?? {}));
      }),
    );
    renderPanel(LINES);
    await vi.waitFor(() =>
      expect(stat('Filament cost').getByText('8.00')).toBeInTheDocument(),
    );
    expect(
      stat('Estimated total including shipping').getByText('8.90'),
    ).toBeInTheDocument();
    const colors = screen.getByRole('table', { name: 'Filament breakdown' });
    expect(within(colors).getByText('6.00')).toBeInTheDocument();
    expect(within(colors).getByText('2.00')).toBeInTheDocument();
  });

  it('keeps totals unavailable after catalog failure and supports keyboard retry', async () => {
    const user = userEvent.setup();
    let fail = true;
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        if (url.includes('/api/v1/filaments') && fail)
          return Promise.resolve(jsonResponse({}, 500));
        const hit = ROUTES.find(([path]) => url.includes(path));
        return Promise.resolve(jsonResponse(hit?.[1] ?? {}));
      }),
    );
    renderPanel(LINES);
    const retry = await screen.findByRole('button', { name: 'Retry totals' });
    expect(
      stat('Estimated total including shipping').getByText('Unavailable'),
    ).toBeInTheDocument();
    fail = false;
    await user.tab();
    expect(retry).toHaveFocus();
    await user.keyboard('{Enter}');
    await vi.waitFor(() =>
      expect(
        stat('Estimated total including shipping').getByText('0.90 (partial)'),
      ).toBeInTheDocument(),
    );
    expect(stat('Filament cost').getByText('Unavailable')).toBeInTheDocument();
  });
});

it('adds area packaging and shipping while inheriting only production costs', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.includes('/api/v1/assemblies/child/explosion'))
        return Promise.resolve(
          jsonResponse({
            printedParts: [],
            nonPrintedComponents: [],
            productionCosts: [
              { catalogItemId: 'labour', category: 'Labour', cost: 2 },
            ],
            finalProductCosts: [
              { catalogItemId: 'shipping', category: 'Shipping', cost: 99 },
            ],
          }),
        );
      if (url.includes('/api/v1/cost-items'))
        return Promise.resolve(
          jsonResponse({
            items: [
              {
                id: 'wrap',
                name: 'Bubble wrap',
                effectiveRate: 0.4,
                pricing: {
                  itemType: 'Physical',
                  category: 'Packaging',
                  unit: 'SquareMetre',
                },
              },
              {
                id: 'shipping',
                name: 'Shipping',
                effectiveRate: 8,
                pricing: {
                  itemType: 'Service',
                  category: 'Shipping',
                  unit: 'Application',
                },
              },
            ],
            totalCount: 2,
          }),
        );
      if (url.includes('/api/v1/filaments'))
        return Promise.resolve(jsonResponse({ filaments: [] }));
      return Promise.resolve(
        jsonResponse({ parts: [], items: [], totalCount: 0 }),
      );
    }),
  );
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const lines: EditableLine[] = [
    {
      key: 'child',
      targetKind: 'Assembly',
      targetId: 'child',
      targetName: 'Child',
      quantity: 3,
    },
  ];
  const { rerender } = render(
    <QueryClientProvider client={client}>
      <BuildPanel
        lines={lines}
        additionalCosts={[
          {
            catalogItemId: 'wrap',
            purpose: 'FinalProduct',
            inputMode: 'Dimensions',
            length: 50,
            lengthUnit: 'cm',
            width: 20,
            widthUnit: 'cm',
            pieces: 1,
          },
          { catalogItemId: 'shipping', purpose: 'FinalProduct', quantity: 1 },
        ]}
      />
    </QueryClientProvider>,
  );
  await vi.waitFor(() =>
    expect(
      stat('Estimated total including shipping').getByText('14.04'),
    ).toBeInTheDocument(),
  );
  expect(stat('Product cost').getByText('6.04')).toBeInTheDocument();
  expect(stat('Labor cost').getByText('6.00')).toBeInTheDocument();
  expect(stat('Shipping allowance').getByText('8.00')).toBeInTheDocument();
  rerender(
    <QueryClientProvider client={client}>
      <BuildPanel
        lines={lines}
        additionalCosts={[
          {
            catalogItemId: 'wrap',
            purpose: 'FinalProduct',
            inputMode: 'Dimensions',
            length: 50,
            lengthUnit: 'cm',
            width: 20,
            widthUnit: 'cm',
            pieces: 2,
          },
        ]}
      />
    </QueryClientProvider>,
  );
  expect(
    stat('Estimated total including shipping').getByText('6.08'),
  ).toBeInTheDocument();
  vi.unstubAllGlobals();
});
