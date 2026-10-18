import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
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
    '/api/v1/non-printed-components',
    {
      items: [
        { id: 'c1', name: 'Magnet', unitCost: 0.25 },
        { id: 'c2', name: 'M3 screw', unitCost: 0.05 },
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
    screen.getByText(label, { selector: 'dt, span' })
      .parentElement as HTMLElement,
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

    const colours = await screen.findByRole('list', {
      name: 'Filament by colour',
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

  it('flags parts without an estimate and components without a cost', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        if (url.includes('/api/v1/parts'))
          return Promise.resolve(
            jsonResponse({ parts: [{ id: 'p1', name: 'Bracket' }] }),
          );
        if (url.includes('/api/v1/non-printed-components'))
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

  it('marks totals as loading while a sub-assembly explodes', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise(() => {})),
    );
    renderPanel(LINES.slice(2, 3));
    expect(screen.getByText('Loading sub-assemblies…')).toBeInTheDocument();
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
        '1 sub-assembly failed to load. Totals incomplete.',
      ),
    ).toBeInTheDocument();
  });
});
