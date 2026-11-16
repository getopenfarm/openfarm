import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { AccessContext } from '@/lib/auth/AccessContext';
import { PrinterDetailPage } from './PrinterDetailPage';

function renderPage(
  printerId: string,
  {
    search = '',
    access,
  }: { search?: string; access?: ReadonlySet<string> } = {},
) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <AccessContext.Provider value={access}>
          <MemoryRouter initialEntries={[`/printers/${printerId}${search}`]}>
            {children}
            <Toaster />
          </MemoryRouter>
        </AccessContext.Provider>
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

  it('marks the printer ready in one click, with no run confirmation', async () => {
    const user = userEvent.setup();
    const printerId = 'finished-printer';
    let markReadyCalls = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        if (url.includes('/hubs/farm'))
          return Promise.reject(new Error('No SignalR in unit tests'));
        if (url.endsWith('/mark-ready')) {
          markReadyCalls++;
          return Promise.resolve(
            jsonResponse({ id: printerId, status: 'Idle' }),
          );
        }
        if (url.includes('/activity'))
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
            feedReadings: [],
          }),
        );
      }),
    );
    renderPage(printerId);
    await screen.findByText('Finished printer');
    await user.click(screen.getByRole('button', { name: 'Mark ready' }));
    await screen.findByText('Printer marked ready.');
    expect(markReadyCalls).toBe(1);
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  function stubPrinterWithStatus(status: string, onMarkNotReady: () => void) {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        if (url.includes('/hubs/farm'))
          return Promise.reject(new Error('No SignalR in unit tests'));
        if (url.endsWith('/mark-not-ready')) {
          onMarkNotReady();
          return Promise.resolve(
            jsonResponse({ id: 'hold', status: 'AwaitingClear' }),
          );
        }
        if (url.includes('/activity'))
          return Promise.resolve(jsonResponse({ items: [] }));
        if (url.includes('/feed-units'))
          return Promise.resolve(jsonResponse({ feedUnits: [] }));
        return Promise.resolve(
          jsonResponse({
            id: 'hold',
            name: 'Hold printer',
            brand: 'System',
            modelCode: 'Simulation',
            status,
            isReachable: true,
            telemetry: {},
            feedReadings: [],
          }),
        );
      }),
    );
  }

  it('lets the operator mark a Ready printer not ready in one click', async () => {
    const user = userEvent.setup();
    let calls = 0;
    stubPrinterWithStatus('Idle', () => calls++);
    renderPage('hold');
    await screen.findByText('Hold printer');
    expect(screen.getByText('Ready')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Mark not ready' }));
    await screen.findByText('Printer marked not ready.');
    expect(calls).toBe(1);
  });

  it('offers Mark ready, not Mark not ready, once the printer is not ready', async () => {
    stubPrinterWithStatus('AwaitingClear', () => {});
    renderPage('hold');
    await screen.findByText('Hold printer');
    expect(screen.getByText('Not ready')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Mark ready' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Mark not ready' }),
    ).not.toBeInTheDocument();
  });

  it('shows a solid Failed chip in the header that marks the printer ready', async () => {
    const user = userEvent.setup();
    let markReadyCalls = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = input.toString();
        if (url.includes('/hubs/farm'))
          return Promise.reject(new Error('No SignalR in unit tests'));
        if (url.endsWith('/mark-ready')) {
          markReadyCalls++;
          return Promise.resolve(jsonResponse({ id: 'p', status: 'Idle' }));
        }
        if (url.includes('/activity'))
          return Promise.resolve(jsonResponse({ items: [] }));
        if (url.includes('/feed-units'))
          return Promise.resolve(jsonResponse({ feedUnits: [] }));
        return Promise.resolve(
          jsonResponse({
            id: 'p',
            name: 'Failed printer',
            brand: 'System',
            modelCode: 'Simulation',
            status: 'AwaitingClear',
            isReachable: true,
            telemetry: { reportedState: 'FAILED' },
            feedReadings: [],
          }),
        );
      }),
    );
    renderPage('p');
    await screen.findByText('Failed printer');
    const chip = screen.getByRole('button', {
      name: 'Mark Failed printer ready',
    });
    expect(chip).toHaveTextContent('Failed');
    expect(chip).toHaveClass('bg-solid-red');
    await user.click(chip);
    await screen.findByText('Printer marked ready.');
    expect(markReadyCalls).toBe(1);
  });

  // Slicer settings: the build plate lives in its own dialog, reachable in every status (it is swapped while printing).
  function stubSlicerPrinter(overrides: Record<string, unknown> = {}) {
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (url.includes('/hubs/farm'))
        return Promise.reject(new Error('No SignalR in unit tests'));
      if (url.endsWith('/slicer-settings') && init?.method === 'PUT')
        return Promise.resolve(jsonResponse({ id: 'sp' }));
      if (url.includes('/activity'))
        return Promise.resolve(jsonResponse({ items: [] }));
      if (url.includes('/feed-units'))
        return Promise.resolve(jsonResponse({ feedUnits: [] }));
      return Promise.resolve(
        jsonResponse({
          id: 'sp',
          name: 'Slicer printer',
          brand: 'Bambu',
          modelCode: 'P1S',
          status: 'Printing',
          isReachable: true,
          telemetry: {},
          feedReadings: [],
          buildPlateType: 'TexturedPei',
          buildPlateTypeLabel: 'Textured PEI Plate',
          compatibleBuildPlateTypes: [
            { value: 'TexturedPei', label: 'Textured PEI Plate' },
            { value: 'HighTemp', label: 'High Temp Plate' },
          ],
          setupIssues: [],
          ...overrides,
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);
    return fetchMock;
  }

  it('changes the build plate from Slicer settings while the printer is printing', async () => {
    const user = userEvent.setup();
    const fetchMock = stubSlicerPrinter();
    renderPage('sp');

    await screen.findByText('Slicer printer');
    expect(screen.getByText('Textured PEI Plate')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Slicer settings' }));
    const dialog = await screen.findByRole('dialog', {
      name: 'Slicer settings',
    });
    expect(dialog).toHaveTextContent('Current print is not affected.');
    const save = within(dialog).getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();

    await user.click(within(dialog).getByRole('combobox'));
    await user.click(
      await screen.findByRole('option', { name: 'High Temp Plate' }),
    );
    expect(dialog).toHaveTextContent(
      'Queued files for Textured PEI Plate stop matching this printer.',
    );
    await user.click(save);

    await screen.findByText('Slicer settings saved.');
    const put = fetchMock.mock.calls.find(
      ([, init]) => (init as RequestInit | undefined)?.method === 'PUT',
    );
    expect(String(put?.[0])).toContain('/api/v1/printers/sp/slicer-settings');
    expect(
      JSON.parse((put as [unknown, RequestInit])[1].body as string),
    ).toEqual({
      buildPlateType: 'HighTemp',
    });
  });

  it('opens Slicer settings from the link ?settings=slicer', async () => {
    stubSlicerPrinter();
    renderPage('sp', { search: '?settings=slicer' });

    expect(
      await screen.findByRole('dialog', { name: 'Slicer settings' }),
    ).toBeInTheDocument();
  });

  it('keeps the Needs setup chip a plain status; the Slicer settings button opens the dialog', async () => {
    const user = userEvent.setup();
    stubSlicerPrinter({
      status: 'Idle',
      buildPlateType: null,
      buildPlateTypeLabel: null,
      setupIssues: ['Select a build plate'],
    });
    renderPage('sp');

    const chip = await screen.findByText('Needs setup');
    expect(chip.closest('button')).toBeNull();
    expect(screen.getByText('Not selected')).toBeInTheDocument();
    expect(screen.getByText('Select a build plate')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Slicer settings' }));
    expect(
      await screen.findByRole('dialog', { name: 'Slicer settings' }),
    ).toBeInTheDocument();
  });

  it('shows the plate read-only without the slicer settings permission', async () => {
    stubSlicerPrinter();
    renderPage('sp', {
      search: '?settings=slicer',
      access: new Set(['printers:get-detail']),
    });

    await screen.findByText('Slicer printer');
    expect(screen.getByText('Textured PEI Plate')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Slicer settings' }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
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
      if (url.includes('/activity')) {
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
                entityType: 'MaintenanceLog',
                entityId: 'related-maintenance',
                eventType: 'MaintenanceCompleted',
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
          feedReadings: [
            {
              kind: 'MultiMaterialUnit',
              unitIndex: 0,
              slots: [
                {
                  slotPosition: 0,
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
        const url = new URL(input.toString(), window.location.origin);
        return url.pathname === `/api/v1/printers/${printerId}/activity`;
      }),
    ).toBe(true);
    expect(screen.getByText('219.4 °C')).toBeInTheDocument();
    expect(screen.getByText('59.8 °C')).toBeInTheDocument();
    // Progress appears in both the telemetry strip and the current-job card.
    expect(screen.getAllByText('42%').length).toBeGreaterThanOrEqual(1);
    expect(await screen.findByText('PLA')).toBeInTheDocument();
    expect(screen.getAllByText('Feed units')).toHaveLength(1);
    expect(screen.queryByText('Filament — AMS')).not.toBeInTheDocument();
    expect(screen.getByText('62%')).toBeInTheDocument();
    // The server-scoped history also includes business events associated with this printer.
    expect(
      await screen.findByText('Printer status changed'),
    ).toBeInTheDocument();
    expect(screen.getByText('Maintenance done')).toBeInTheDocument();
    expect(screen.queryByText('Slicer readiness')).not.toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(([url]) =>
        String(url).includes('/slicing/readiness'),
      ),
    ).toBe(false);
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
      if (url.includes('/activity')) {
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
          feedReadings: [],
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage(printerId);

    expect(await screen.findByText('Idle Printer')).toBeInTheDocument();
    expect(screen.getByText('No active print')).toBeInTheDocument();
    expect(await screen.findByText('No feed units')).toBeInTheDocument();
    expect(await screen.findByText('No recent activity')).toBeInTheDocument();
  });

  it('renders the backend-resolved build plate label when one is selected', async () => {
    const printerId = '66666666-6666-6666-6666-666666666666';
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      if (url.includes('/activity')) {
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
          feedReadings: [],
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
      if (url.includes('/activity')) {
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
          feedReadings: [],
          buildPlateType: null,
          buildPlateTypeLabel: null,
          setupIssues: ['Select a build plate'],
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
    // The printer cannot take a job yet: the badge and the card say so, instead of a plain Ready.
    expect(screen.getByText('Needs setup')).toBeInTheDocument();
    expect(screen.queryByText('Ready')).not.toBeInTheDocument();
    expect(screen.getByText('Select a build plate')).toBeInTheDocument();
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
      if (url.includes('/activity')) {
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
          feedReadings: [],
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
      if (url.includes('/activity')) {
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
          feedReadings: [],
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
