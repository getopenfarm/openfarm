import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { PrinterDetailPage } from './PrinterDetailPage';

function renderPage(printerId: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/printers/${printerId}`]}>
          {children}
          <Toaster />
        </MemoryRouter>
      </QueryClientProvider>
    );
  }

  return render(
    <Routes>
      <Route path="/printers/:id" element={<PrinterDetailPage />} />
      <Route path="/printers" element={<div>Fleet list page</div>} />
    </Routes>,
    { wrapper: Wrapper },
  );
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('PrinterDetailPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('requires explicit confirmation before marking the last named-run printer ready', async () => {
    const user = userEvent.setup();
    const printerId = 'named-run-printer';
    const requests: boolean[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = input.toString();
        if (url.includes('/hubs/farm'))
          return Promise.reject(new Error('No SignalR in unit tests'));
        if (url.endsWith('/mark-ready')) {
          const confirmed = JSON.parse(String(init?.body)).confirmRunCompletion;
          requests.push(confirmed);
          return Promise.resolve(
            confirmed
              ? jsonResponse({ id: printerId, status: 'Idle' })
              : jsonResponse(
                  { title: 'Run completion confirmation required' },
                  409,
                ),
          );
        }
        if (url.includes('/activity-log'))
          return Promise.resolve(jsonResponse({ items: [] }));
        if (url.includes('/feed-units'))
          return Promise.resolve(jsonResponse({ feedUnits: [] }));
        return Promise.resolve(
          jsonResponse({
            id: printerId,
            name: 'Finished printer',
            brand: 'System',
            modelCode: 'Simulation',
            status: 'AwaitingClear',
            isReachable: true,
            telemetry: {},
            amsUnits: [],
          }),
        );
      }),
    );
    renderPage(printerId);
    await screen.findByText('Finished printer');
    await user.click(screen.getByRole('button', { name: 'Mark ready' }));
    const dialog = await screen.findByRole('alertdialog');
    expect(requests).toEqual([false]);
    await user.click(
      within(dialog).getByRole('button', {
        name: 'Mark ready',
      }),
    );
    await screen.findByText('Printer marked ready.');
    expect(requests).toEqual([false, true]);
  });

  it('renders telemetry, current job, AMS and activity for the printer', async () => {
    const printerId = '11111111-1111-1111-1111-111111111111';
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      if (url.includes('/feed-units')) {
        return Promise.resolve(
          jsonResponse({
            feedUnits: [
              {
                id: 'ams-1',
                kind: 'MultiMaterialUnit',
                unitIndex: 0,
                displayLabel: 'AMS 1',
                feedSlots: [
                  {
                    id: 'slot-1',
                    slotPosition: 0,
                    displayLabel: 'AMS 1 · Slot 1',
                  },
                ],
              },
            ],
          }),
        );
      }
      if (url.includes('/activity-log')) {
        return Promise.resolve(
          jsonResponse({
            items: [
              {
                id: 'a1',
                entityType: 'Printer',
                entityId: printerId,
                eventType: 'PrinterStatusChanged',
                actorType: 'System',
                timestampUtc: new Date().toISOString(),
              },
              {
                id: 'a2',
                entityType: 'Job',
                entityId: 'other',
                eventType: 'JobQueued',
                actorType: 'System',
                timestampUtc: new Date().toISOString(),
              },
            ],
            page: 1,
            pageSize: 20,
            totalCount: 2,
          }),
        );
      }
      return Promise.resolve(
        jsonResponse({
          id: printerId,
          name: 'Detail Test Printer',
          brand: 'Bambu',
          modelCode: 'X1C',
          ipAddress: '10.0.0.60',
          serialNumber: 'SN-DETAIL-1',
          status: 'Printing',
          isReachable: true,
          telemetry: {
            nozzleTempC: 219.4,
            bedTempC: 59.8,
            printProgressPercent: 42,
            currentStageLabel: 'printing',
          },
          amsUnits: [
            {
              unitIndex: 0,
              trays: [
                {
                  slotIndex: 0,
                  empty: false,
                  materialType: 'PLA',
                  colorHex: '1A1A1AFF',
                  remainPercent: 62,
                  active: true,
                },
              ],
            },
          ],
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage(printerId);

    expect(await screen.findByText('Detail Test Printer')).toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(([input]) => {
        const url = new URL(input.toString());
        return (
          url.pathname.endsWith('/activity-log') &&
          url.searchParams.get('entityType') === 'Printer' &&
          url.searchParams.get('entityId') === printerId
        );
      }),
    ).toBe(true);
    expect(screen.getByText('219.4°C')).toBeInTheDocument();
    expect(screen.getByText('59.8°C')).toBeInTheDocument();
    // Progress appears in both the telemetry strip and the current-job card.
    expect(screen.getAllByText('42%').length).toBeGreaterThanOrEqual(1);
    expect(await screen.findByText('PLA')).toBeInTheDocument();
    expect(screen.getAllByText('Feed Units')).toHaveLength(1);
    expect(screen.queryByText('Filament — AMS')).not.toBeInTheDocument();
    expect(screen.getByText('Printer estimate: 62%')).toBeInTheDocument();
    // Only the Printer-scoped activity entry is shown, not the unrelated Job one.
    expect(
      await screen.findByText('Printer status changed'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Job queued')).not.toBeInTheDocument();
  });

  it('shows a graceful empty state when the printer is idle', async () => {
    const printerId = '33333333-3333-3333-3333-333333333333';
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      if (url.includes('/feed-units'))
        return Promise.resolve(jsonResponse({ feedUnits: [] }));
      if (url.includes('/activity-log')) {
        return Promise.resolve(
          jsonResponse({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
        );
      }
      return Promise.resolve(
        jsonResponse({
          id: printerId,
          name: 'Idle Printer',
          brand: 'Bambu',
          modelCode: 'P1S',
          ipAddress: '10.0.0.61',
          serialNumber: 'SN-IDLE-1',
          status: 'Idle',
          isReachable: true,
          telemetry: {
            nozzleTempC: 24,
            bedTempC: 24,
            printProgressPercent: null,
            currentStageLabel: null,
          },
          amsUnits: [],
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage(printerId);

    expect(await screen.findByText('Idle Printer')).toBeInTheDocument();
    expect(screen.getByText('No active print')).toBeInTheDocument();
    expect(
      await screen.findByText('No Feed Units on this printer'),
    ).toBeInTheDocument();
    expect(await screen.findByText('No recent activity')).toBeInTheDocument();
  });

  it('renders the backend-resolved build plate label when one is selected', async () => {
    const printerId = '66666666-6666-6666-6666-666666666666';
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      if (url.includes('/activity-log')) {
        return Promise.resolve(
          jsonResponse({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
        );
      }
      return Promise.resolve(
        jsonResponse({
          id: printerId,
          name: 'Build Plate Printer',
          brand: 'Bambu',
          modelCode: 'A1',
          ipAddress: '10.0.0.64',
          serialNumber: 'SN-PLATE-1',
          status: 'Idle',
          isReachable: true,
          telemetry: {
            nozzleTempC: 24,
            bedTempC: 24,
            printProgressPercent: null,
            currentStageLabel: null,
          },
          amsUnits: [],
          buildPlateType: 'TexturedPei',
          buildPlateTypeLabel: 'Textured PEI Plate',
          compatibleBuildPlateTypes: [
            { value: 'TexturedPei', label: 'Textured PEI Plate' },
            { value: 'HighTemp', label: 'High Temp Plate' },
          ],
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage(printerId);

    expect(await screen.findByText('Textured PEI Plate')).toBeInTheDocument();
  });

  it('shows "Not selected" for the build plate row when none has been chosen yet', async () => {
    const printerId = '77777777-7777-7777-7777-777777777777';
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      if (url.includes('/activity-log')) {
        return Promise.resolve(
          jsonResponse({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
        );
      }
      return Promise.resolve(
        jsonResponse({
          id: printerId,
          name: 'No Plate Printer',
          brand: 'Bambu',
          modelCode: 'A1',
          ipAddress: '10.0.0.65',
          serialNumber: 'SN-PLATE-2',
          status: 'Idle',
          isReachable: true,
          telemetry: {
            nozzleTempC: 24,
            bedTempC: 24,
            printProgressPercent: null,
            currentStageLabel: null,
          },
          amsUnits: [],
          buildPlateType: null,
          buildPlateTypeLabel: null,
          compatibleBuildPlateTypes: [
            { value: 'TexturedPei', label: 'Textured PEI Plate' },
            { value: 'HighTemp', label: 'High Temp Plate' },
          ],
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage(printerId);

    expect(await screen.findByText('No Plate Printer')).toBeInTheDocument();
    expect(screen.getByText('Not selected')).toBeInTheDocument();
  });

  it('shows a not-found message for a missing printer', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      return Promise.resolve(new Response(null, { status: 404 }));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage('22222222-2222-2222-2222-222222222222');

    expect(await screen.findByText('Printer not found')).toBeInTheDocument();
  });

  it('shows a working Remove action that navigates back to the fleet list (SC-003/SC-004)', async () => {
    const printerId = '44444444-4444-4444-4444-444444444444';
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      if (url.includes('/activity-log')) {
        return Promise.resolve(
          jsonResponse({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
        );
      }
      if (init?.method === 'DELETE') {
        return Promise.resolve(new Response(null, { status: 204 }));
      }
      return Promise.resolve(
        jsonResponse({
          id: printerId,
          name: 'Removable Printer',
          brand: 'Bambu',
          modelCode: 'P1S',
          ipAddress: '10.0.0.62',
          serialNumber: 'SN-REMOVE-1',
          status: 'Idle',
          isReachable: true,
          telemetry: {
            nozzleTempC: 24,
            bedTempC: 24,
            printProgressPercent: null,
            currentStageLabel: null,
          },
          amsUnits: [],
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage(printerId);

    expect(await screen.findByText('Removable Printer')).toBeInTheDocument();
    const removeButton = screen.getByRole('button', { name: /remove/i });
    await user.click(removeButton);

    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText('Remove printer?')).toBeInTheDocument();
    const confirmButton = within(dialog).getByRole('button', {
      name: 'Remove',
    });
    await user.click(confirmButton);

    expect(await screen.findByText('Fleet list page')).toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(
        (call) =>
          call[0].toString().includes(`/api/v1/printers/${printerId}`) &&
          (call[1] as RequestInit | undefined)?.method === 'DELETE',
      ),
    ).toBe(true);
  });

  it('does not remove the printer when the confirmation is declined', async () => {
    const printerId = '55555555-5555-5555-5555-555555555555';
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      if (url.includes('/activity-log')) {
        return Promise.resolve(
          jsonResponse({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
        );
      }
      if (init?.method === 'DELETE') {
        throw new Error(
          'DELETE should not be called when confirmation is declined',
        );
      }
      return Promise.resolve(
        jsonResponse({
          id: printerId,
          name: 'Kept Printer',
          brand: 'Bambu',
          modelCode: 'P1S',
          ipAddress: '10.0.0.63',
          serialNumber: 'SN-KEEP-1',
          status: 'Idle',
          isReachable: true,
          telemetry: {
            nozzleTempC: 24,
            bedTempC: 24,
            printProgressPercent: null,
            currentStageLabel: null,
          },
          amsUnits: [],
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage(printerId);

    expect(await screen.findByText('Kept Printer')).toBeInTheDocument();
    const removeButton = screen.getByRole('button', { name: /remove/i });
    await user.click(removeButton);

    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText('Remove printer?')).toBeInTheDocument();
    const cancelButton = within(dialog).getByRole('button', {
      name: 'Cancel',
    });
    await user.click(cancelButton);

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.getByText('Kept Printer')).toBeInTheDocument();
  });
});
