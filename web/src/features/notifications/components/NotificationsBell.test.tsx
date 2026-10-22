import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { NotificationsBell } from './NotificationsBell';

const ITEM = {
  type: 'FilamentNotDeducted',
  printId: 'print-1',
  name: 'benchy.gcode.3mf',
  source: 'External',
  status: 'Failed',
  printerId: 'printer-1',
  endedAtUtc: new Date().toISOString(),
  lines: [
    {
      lineId: 'line-1',
      toolIndex: 0,
      materialType: 'PLA',
      colorHex: '000000',
      estimatedGrams: 20,
      reason: 'NoSpool',
    },
    {
      lineId: 'line-2',
      toolIndex: 1,
      materialType: 'PETG',
      colorHex: 'ffffff',
      estimatedGrams: 5,
      reason: 'NoEstimate',
    },
  ],
};

function stubFetch(
  items: unknown[],
  lowStock: unknown[] = [],
  rfidSpools: unknown[] = [],
) {
  const requests: { url: string; method?: string }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      requests.push({ url, method: init?.method });
      const json = (body: unknown) =>
        Promise.resolve(
          new Response(JSON.stringify(body), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
      if (url.includes('/dismiss')) return json({ id: 'print-1' });
      if (url.includes('/rfid')) return json({ id: 'spool-untagged' });
      if (url.includes('/api/v1/notifications'))
        return json({ items, totalCount: items.length, lowStock, rfidSpools });
      if (url.includes('/api/v1/printers'))
        return json({ printers: [{ id: 'printer-1', name: 'Bambu A1' }] });
      if (url.includes('/api/v1/spools'))
        return json({ spools: [], totalCount: 0 });
      if (url.includes('/api/v1/filaments')) return json({ filaments: [] });
      return json({});
    }),
  );
  return requests;
}

function renderBell() {
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
      <MemoryRouter>
        <NotificationsBell />
        <Toaster />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('NotificationsBell', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows no count and says there is nothing to do when the list is empty', async () => {
    stubFetch([]);
    renderBell();
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole('button', { name: 'Notifications' }),
    );

    expect(await screen.findByText('Action required')).toBeInTheDocument();
    expect(screen.getByText('Nothing to do')).toBeInTheDocument();
  });

  it('counts the items and lists each print with its lines and reasons', async () => {
    stubFetch([ITEM]);
    renderBell();
    const user = userEvent.setup();

    const bell = await screen.findByRole('button', {
      name: 'Notifications, 1 need action',
    });
    expect(bell).toHaveTextContent('1');
    await user.click(bell);

    const link = await screen.findByRole('link', { name: 'benchy.gcode.3mf' });
    expect(link).toHaveAttribute('href', '/jobs/print-1');
    expect(await screen.findByText(/Bambu A1 · Failed/)).toBeInTheDocument();
    expect(screen.getByText('No spool')).toBeInTheDocument();
    expect(screen.getByText('No estimate')).toBeInTheDocument();
    expect(screen.getByText('20 g')).toBeInTheDocument();
  });

  it('offers Assign spool only for a line a spool can fix', async () => {
    stubFetch([ITEM]);
    renderBell();
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole('button', { name: /^Notifications, 1/ }),
    );

    expect(
      await screen.findAllByRole('button', { name: /^Assign spool to/ }),
    ).toHaveLength(1);
  });

  it('closes the popover and opens the assign dialog for the chosen line', async () => {
    stubFetch([ITEM]);
    renderBell();
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole('button', { name: /^Notifications, 1/ }),
    );
    await user.click(
      await screen.findByRole('button', { name: /^Assign spool to PLA/ }),
    );

    const dialog = await screen.findByRole('dialog', { name: 'Assign spool' });
    expect(
      within(dialog).getByText(/benchy\.gcode\.3mf · PLA #000000/),
    ).toBeVisible();
    expect(screen.queryByText('Action required')).not.toBeInTheDocument();
  });

  it('dismisses an item', async () => {
    const requests = stubFetch([ITEM]);
    renderBell();
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole('button', { name: /^Notifications, 1/ }),
    );
    await user.click(
      await screen.findByRole('button', { name: 'Dismiss benchy.gcode.3mf' }),
    );

    await waitFor(() =>
      expect(
        requests.some(
          (request) =>
            request.method === 'POST' &&
            request.url.includes('/api/v1/notifications/print-1/dismiss'),
        ),
      ).toBe(true),
    );
    expect(await screen.findByText('Dismissed.')).toBeInTheDocument();
  });
  it('counts and lists colors low on stock, linking to their filament', async () => {
    stubFetch(
      [ITEM],
      [
        {
          filamentId: 'f-1',
          filamentVariantId: 'var-red',
          vendorName: 'Bambu',
          material: 'PLA Basic',
          colorName: 'Red',
          colorHex: '#ff0000',
          onHandGrams: 320,
          thresholdGrams: 1500,
        },
      ],
    );
    renderBell();
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole('button', {
        name: 'Notifications, 2 need action',
      }),
    );

    expect(
      await screen.findByRole('link', { name: 'Bambu · PLA Basic · Red' }),
    ).toHaveAttribute('href', '/filament/f-1/edit');
    expect(
      screen.getByText('320 g on hand · below 1.5 kg'),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Dismiss Bambu/ }),
    ).not.toBeInTheDocument();
  });
  it('lists an unregistered RFID spool with Register prefilled, and links its tag to the untagged spool in the slot', async () => {
    const requests = stubFetch(
      [],
      [],
      [
        {
          printerId: 'printer-1',
          printerName: 'P01',
          feedSlotId: 'slot-2',
          slotLabel: 'AMS 1 · Slot 2',
          rfidId: 'TAG-1',
          materialType: 'PLA',
          subBrand: 'PLA Matte',
          colorHex: '080808FF',
          remainPercent: 87,
          rfidWeightGrams: 1000,
          remainingGrams: 870,
          filamentVariantId: 'var-1',
          variantName: 'PLA Matte Charcoal',
          loadedSpoolId: 'spool-untagged',
          loadedSpoolName: 'Shelf B',
        },
      ],
    );
    renderBell();
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole('button', {
        name: 'Notifications, 1 need action',
      }),
    );

    expect(
      await screen.findByText('Unregistered RFID spool'),
    ).toBeInTheDocument();
    expect(screen.getByText('P01 · AMS 1 · Slot 2')).toBeInTheDocument();
    expect(screen.getByText('PLA Matte Charcoal · 87%')).toBeInTheDocument();
    const register = new URL(
      screen.getByRole('link', { name: 'Register' }).getAttribute('href') ?? '',
      'http://localhost',
    );
    expect(register.pathname).toBe('/spools/new');
    expect(Object.fromEntries(register.searchParams)).toEqual({
      rfidId: 'TAG-1',
      slot: 'P01 · AMS 1 · Slot 2',
      variantId: 'var-1',
      spoolWeightGrams: '1000',
      remainingWeightGrams: '870',
    });

    expect(screen.getByText('Loaded without tag: Shelf B')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Link' }));

    await waitFor(() =>
      expect(requests).toContainEqual({
        url: expect.stringContaining('/api/v1/spools/spool-untagged/rfid'),
        method: 'PUT',
      }),
    );
    expect(await screen.findByText('Spool linked.')).toBeInTheDocument();
  });
});
