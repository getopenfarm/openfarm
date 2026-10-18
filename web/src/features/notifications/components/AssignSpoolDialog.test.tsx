import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { AssignSpoolDialog } from './AssignSpoolDialog';

const ITEM = { printId: 'print-1', name: 'benchy.gcode.3mf' };
const LINE = {
  lineId: 'line-1',
  materialType: 'PLA',
  colorHex: '000000',
  estimatedGrams: 20,
  reason: 'NoSpool',
};
const SPOOLS = [
  {
    id: 'spool-1',
    filamentVariantId: 'v1',
    vendorName: 'Bambu Lab',
    material: 'PLA Basic',
    colorName: 'Black',
    colorHex: '#000000',
    spoolWeightGrams: 1000,
    remainingWeightGrams: 400,
    state: 'Using',
  },
  {
    id: 'spool-2',
    filamentVariantId: 'v2',
    vendorName: 'Polymaker',
    material: 'PETG',
    colorName: 'Red',
    colorHex: '#ff0000',
    spoolWeightGrams: 1000,
    remainingWeightGrams: 90,
    state: 'Using',
  },
];

function stubFetch({
  spools = SPOOLS,
  assign = { status: 200, body: { gramsDeducted: 20 } },
}: {
  spools?: typeof SPOOLS;
  assign?: { status: number; body: unknown };
} = {}) {
  const requests: { url: string; method?: string; body?: unknown }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      requests.push({
        url,
        method: init?.method,
        body: init?.body ? JSON.parse(init.body as string) : undefined,
      });
      const json = (body: unknown, status = 200) =>
        Promise.resolve(
          new Response(JSON.stringify(body), {
            status,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
      if (init?.method === 'POST') return json(assign.body, assign.status);
      if (url.includes('/api/v1/spools'))
        return json({ spools, totalCount: spools.length });
      if (url.includes('/api/v1/filaments')) return json({ filaments: [] });
      return json({});
    }),
  );
  return requests;
}

function renderDialog(onOpenChange = vi.fn()) {
  render(
    <QueryClientProvider
      client={
        new QueryClient({
          defaultOptions: {
            queries: { retry: false },
            mutations: { retry: false },
          },
        })
      }
    >
      <AssignSpoolDialog item={ITEM} line={LINE} onOpenChange={onOpenChange} />
      <Toaster />
    </QueryClientProvider>,
  );
  return onOpenChange;
}

describe('AssignSpoolDialog', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('offers only loaded spools and keeps Assign disabled until one is chosen', async () => {
    const requests = stubFetch();
    renderDialog();

    expect(
      await screen.findByRole('radio', { name: /Bambu Lab PLA Basic/ }),
    ).toBeInTheDocument();
    expect(
      requests.some(
        (request) =>
          request.url.includes('/api/v1/spools') &&
          new URL(request.url, 'http://localhost').searchParams.get('state') ===
            '1',
      ),
    ).toBe(true);
    expect(screen.getByText(/benchy\.gcode\.3mf · PLA #000000/)).toBeVisible();
    expect(
      screen.getByRole('button', { name: 'Assign and deduct' }),
    ).toBeDisabled();
  });

  it('assigns the chosen spool, confirms the grams and closes', async () => {
    const requests = stubFetch();
    const onOpenChange = renderDialog();
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole('radio', { name: /Bambu Lab PLA Basic/ }),
    );
    await user.click(screen.getByRole('button', { name: 'Assign and deduct' }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    const post = requests.find((request) => request.method === 'POST');
    expect(post?.url).toContain(
      '/api/v1/prints/print-1/filament-lines/line-1/spool',
    );
    expect(post?.body).toEqual({ spoolId: 'spool-1' });
    expect(
      await screen.findByText('Deducted 20 g from Bambu Lab PLA Basic Black.'),
    ).toBeInTheDocument();
  });

  it('shows the server reason and stays open when the spool is refused', async () => {
    stubFetch({
      assign: { status: 409, body: { detail: 'The spool is not loaded.' } },
    });
    const onOpenChange = renderDialog();
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole('radio', { name: /Polymaker PETG/ }),
    );
    await user.click(screen.getByRole('button', { name: 'Assign and deduct' }));

    expect(
      await screen.findByText('The spool is not loaded.'),
    ).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it('says so when no spool is loaded', async () => {
    stubFetch({ spools: [] });
    renderDialog();

    expect(await screen.findByText('No loaded spools')).toBeInTheDocument();
  });
});
