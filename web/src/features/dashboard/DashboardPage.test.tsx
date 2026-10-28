import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { farmConnectionQueryKey } from '@/features/printers/hooks/useFarmHub';
import type {
  FeedUnitResponse,
  FleetPrinterResponse,
  PrinterDetailResponse,
  SpoolResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { DashboardPage } from './DashboardPage';

const p1s = { cameraSupported: true };
const printing: FleetPrinterResponse = {
  id: 'p1',
  name: 'P1S-01',
  status: 'Printing',
  isReachable: true,
  modelCode: 'P1S',
  model: p1s,
  currentJobName: 'Enclosure bracket',
  printProgressPercent: 62,
  remainingTimeMinutes: 84,
};
const finished: FleetPrinterResponse = {
  id: 'p2',
  name: 'P1S-02',
  status: 'AwaitingClear',
  isReachable: true,
  modelCode: 'P1S',
  model: p1s,
  currentJobName: 'Cable organizer',
  printProgressPercent: 100,
};
const idle: FleetPrinterResponse = {
  id: 'p3',
  name: 'P1S-03',
  status: 'Idle',
  isReachable: true,
  modelCode: 'P1S',
  model: p1s,
};
function respond(data: unknown, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}
function setup(
  printers = [printing, finished, idle],
  overrides: Record<string, Partial<PrinterDetailResponse>> = {},
  options: {
    fleetStatus?: number;
    feeds?: FeedUnitResponse[];
    spools?: SpoolResponse[];
  } = {},
) {
  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.endsWith('/mark-ready') && init?.method === 'POST')
      return respond({});
    if (url.endsWith('/feed-units'))
      return respond({ feedUnits: options.feeds ?? [] });
    if (url.includes('/api/v1/spools?'))
      return respond({ spools: options.spools ?? [] });
    if (url.endsWith('/filaments')) return respond({ filaments: [] });
    const detail = printers.find((printer) =>
      url.endsWith(`/printers/${printer.id}`),
    );
    if (detail)
      return respond({
        ...detail,
        telemetry: {
          reportedState:
            detail.status === 'Printing'
              ? 'RUNNING'
              : detail.status === 'AwaitingClear'
                ? 'FINISH'
                : 'IDLE',
        },
        ...overrides[detail.id ?? ''],
      });
    return respond({ printers }, options.fleetStatus ?? 200);
  });
  vi.stubGlobal('fetch', fetch);
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  client.setQueryData(farmConnectionQueryKey, 'connected');
  return {
    ...render(
      <QueryClientProvider client={client}>
        <MemoryRouter>
          <DashboardPage />
        </MemoryRouter>
      </QueryClientProvider>,
    ),
    fetch,
    client,
  };
}
beforeEach(() => localStorage.clear());
afterEach(() => vi.unstubAllGlobals());

describe('Fixed compact dashboard', () => {
  it('shows every printer with job/ETA and linked name, without removed controls', async () => {
    localStorage.setItem(
      'openfarm.dashboard.v1:farm-a',
      JSON.stringify({ printerIds: [], view: 'compact', showFilament: false }),
    );
    setup();
    await screen.findByRole('article', { name: 'P1S-01 monitor' });
    expect(screen.getAllByRole('article')).toHaveLength(3);
    expect(screen.getByText('1h 24m left')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'P1S-01' })).toHaveAttribute(
      'href',
      '/printers/p1',
    );
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', {
        name: /Customize|Compact|Cameras|Inspect|Expand/,
      }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('region', { name: 'Needs attention' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText('Live updates connected'),
    ).not.toBeInTheDocument();
    expect(screen.queryByText('Maintenance due')).not.toBeInTheDocument();
    expect(
      within(
        screen.getByRole('article', { name: 'P1S-03 monitor' }),
      ).queryByRole('button', { name: /Mark.*ready/ }),
    ).not.toBeInTheDocument();
  });
  it('groups errors and warnings inside the card and reveals full messages on focus or tap', async () => {
    setup([printing], {
      p1: {
        telemetry: {
          reportedState: 'PAUSE',
          activeAlerts: [
            { severity: 'Warning', message: 'Filament runout' },
            { severity: 'Error', message: 'Motor error' },
          ],
        },
      },
    });
    const issue = await screen.findByRole('button', {
      name: 'P1S-01: Motor error · Filament runout',
    });
    const card = screen.getByRole('article', { name: 'P1S-01 monitor' });
    expect(card).toHaveTextContent('Paused');
    expect(card).toHaveTextContent('+1');
    expect(card).not.toHaveTextContent('1h 24m left');
    fireEvent.focus(issue);
    expect(screen.getByRole('dialog')).toHaveTextContent('Filament runout');
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' });
    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
    );
    fireEvent.click(issue);
    expect(screen.getByRole('dialog')).toHaveTextContent('Motor error');
  });
  it('shows farm status, not the reported FINISH state, on the chip', async () => {
    setup([finished]);
    const card = await screen.findByRole('article', {
      name: 'P1S-02 monitor',
    });
    expect(card).toHaveTextContent('Awaiting clear');
    expect(card).not.toHaveTextContent('Finished');
  });
  it('marks ready from the status chip in one click', async () => {
    const { fetch } = setup([finished]);
    const markReady = await screen.findByRole('button', {
      name: 'Mark P1S-02 ready',
    });
    expect(
      markReady.querySelector('[data-slot="status-badge"]'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Clear bed')).not.toBeInTheDocument();
    expect(screen.queryByText('Mark ready')).not.toBeInTheDocument();
    fireEvent.click(markReady);
    await waitFor(() =>
      expect(
        fetch.mock.calls.filter(
          ([url, init]) =>
            String(url).endsWith('/mark-ready') && init?.method === 'POST',
        ),
      ).toHaveLength(1),
    );
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });
  it('shows bars instead of persistent filament values and reveals recorded weight on hover', async () => {
    setup(
      [printing],
      {
        p1: {
          feedReadings: [
            {
              kind: 'ExternalSpool',
              unitIndex: 0,
              displayLabel: 'External Spool',
              slots: [{ slotPosition: 0, active: true, remainPercent: 20 }],
            },
          ],
        },
      },
      {
        feeds: [
          {
            id: 'ext',
            kind: 'ExternalSpool',
            unitIndex: 0,
            feedSlots: [
              {
                id: 'slot',
                slotPosition: 0,
                currentSpool: { id: 's1', remainingWeightGrams: 328 },
              },
            ],
          },
        ],
        spools: [
          { id: 's1', remainingWeightGrams: 328, spoolWeightGrams: 500 },
        ],
      },
    );
    const meter = await screen.findByRole('meter', {
      name: 'External spool · Slot 1 remaining',
    });
    expect(Number(meter.getAttribute('value'))).toBeCloseTo(65.6);
    expect(screen.queryByText(/328 g recorded/)).not.toBeInTheDocument();
    fireEvent.pointerEnter(
      screen.getByRole('button', {
        name: /External spool · Slot 1: 328 g recorded/,
      }),
    );
    expect(screen.getByRole('dialog')).toHaveTextContent(
      '328 g recorded / 500 g capacity',
    );
    expect(screen.getByRole('dialog')).toHaveTextContent(
      'Printer estimate: 20%',
    );
  });
  describe('filament rail popover', () => {
    const ams: FeedUnitResponse = {
      id: 'ams',
      kind: 'MultiMaterialUnit',
      unitIndex: 0,
      displayLabel: 'AMS 1',
      feedSlots: [
        {
          id: 'slot-1',
          slotPosition: 0,
          displayLabel: 'AMS 1 · Slot 1',
          currentSpool: { id: 's1', remainingWeightGrams: 300 },
        },
        { id: 'slot-2', slotPosition: 1, displayLabel: 'AMS 1 · Slot 2' },
      ],
    };
    const spools = [
      { id: 's1', remainingWeightGrams: 300, spoolWeightGrams: 500 },
    ];
    // Bambu reports all four trays; slots 3 and 4 exist only in telemetry.
    const reported = {
      p1: {
        feedReadings: [
          {
            kind: 'MultiMaterialUnit',
            unitIndex: 0,
            displayLabel: 'AMS 1',
            slots: [0, 1, 2, 3].map((slotPosition) => ({
              slotPosition,
              displayLabel: `AMS 1 · Slot ${slotPosition + 1}`,
            })),
          },
        ],
      },
    };
    async function bars() {
      const [one, two, three] = await Promise.all([
        screen.findByRole('button', { name: /^AMS 1 · Slot 1:/ }),
        screen.findByRole('button', { name: /^AMS 1 · Slot 2:/ }),
        screen.findByRole('button', { name: /^AMS 1 · Slot 3:/ }),
      ]);
      return { one, two, three };
    }

    it('shows one popover that follows hover while nothing is pinned', async () => {
      setup([printing], reported, { feeds: [ams], spools });
      const { one, two } = await bars();
      fireEvent.pointerEnter(two);
      expect(screen.getAllByRole('dialog')).toHaveLength(1);
      expect(screen.getByRole('dialog')).toHaveTextContent('AMS 1 · Slot 2');
      fireEvent.pointerEnter(one);
      expect(screen.getAllByRole('dialog')).toHaveLength(1);
      expect(screen.getByRole('dialog')).toHaveTextContent('AMS 1 · Slot 1');
    });

    it('hints at managing on hover and opens the slot dialog on click', async () => {
      setup([printing], reported, { feeds: [ams], spools });
      const { two } = await bars();
      fireEvent.pointerEnter(two);
      expect(screen.getByRole('dialog')).toHaveTextContent(
        'Click to manage slot.',
      );
      fireEvent.click(two);
      expect(
        await screen.findByText('Empty. Choose a spool to load.'),
      ).toBeInTheDocument();
      expect(screen.getAllByRole('dialog')).toHaveLength(1);
      expect(screen.getByRole('dialog')).toHaveTextContent('AMS 1 · Slot 2');
    });

    it('opens the slot dialog for a loaded slot with its unload actions', async () => {
      setup([printing], reported, { feeds: [ams], spools });
      const { one } = await bars();
      fireEvent.click(one);
      expect(
        await screen.findByRole('button', { name: 'Return to storage' }),
      ).toBeInTheDocument();
      expect(
        screen.getByRole('button', { name: 'Mark finished' }),
      ).toBeInTheDocument();
    });

    it('only shows the readout for a slot that exists only in telemetry', async () => {
      setup([printing], reported, { feeds: [ams], spools });
      const { three } = await bars();
      fireEvent.click(three);
      expect(screen.getByRole('dialog')).toHaveTextContent('AMS 1 · Slot 3');
      expect(screen.getByRole('dialog')).not.toHaveTextContent(
        'Click to manage slot.',
      );
      expect(
        screen.queryByRole('button', { name: 'Cancel' }),
      ).not.toBeInTheDocument();
    });
  });
  it('keeps unknown and empty quantities distinct and shows the active AMS without persistent equipment labels', async () => {
    setup([printing], {
      p1: {
        feedReadings: [
          {
            kind: 'MultiMaterialUnit',
            unitIndex: 0,
            displayLabel: 'AMS 1',
            slots: [{ slotPosition: 0, empty: true }],
          },
          {
            kind: 'MultiMaterialUnit',
            unitIndex: 1,
            displayLabel: 'AMS 2',
            slots: [
              { slotPosition: 0, active: true, remainPercent: 50 },
              { slotPosition: 1, empty: true },
              { slotPosition: 2 },
              { slotPosition: 3 },
            ],
          },
        ],
      },
    });
    await screen.findByRole('meter', { name: 'AMS 2 · Slot 1 remaining' });
    expect(screen.queryByText('+1 AMS')).not.toBeInTheDocument();
    expect(screen.queryByText('AMS 2')).not.toBeInTheDocument();
    expect(
      screen.getByRole('meter', { name: 'AMS 2 · Slot 1 remaining' }),
    ).toHaveAttribute('value', '50');
    expect(
      screen.getByRole('meter', { name: 'AMS 2 · Slot 2 remaining' }),
    ).toHaveAttribute('value', '0');
    expect(
      screen.getByRole('button', {
        name: 'AMS 2 · Slot 3: Remaining quantity unknown',
      }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('meter', { name: 'AMS 2 · Slot 3 remaining' }),
    ).not.toBeInTheDocument();
  });
  it('retains offline job identity without showing current ETA or clearing actions', async () => {
    setup([{ ...printing, isReachable: false }]);
    await screen.findByText('Enclosure bracket');
    expect(screen.getAllByText('Offline')).toHaveLength(2);
    expect(screen.queryByText('1h 24m left')).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: /Mark.*ready/ }),
    ).not.toBeInTheDocument();
  });
  it('shows real empty and failed-load states separately', async () => {
    const { unmount } = setup([]);
    await screen.findByText('Farm is empty');
    unmount();
    setup([], {}, { fleetStatus: 500 });
    await screen.findByText('Could not load dashboard.');
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
  });
});
