import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey } from '@/lib/api/generated/printers/printers';
import { EditPrinterDialog } from './EditPrinterDialog';

const PRINTER_ID = '11111111-1111-1111-1111-111111111111';

function renderDialog(
  onOpenChange: (open: boolean) => void = () => {},
  overrides: Partial<React.ComponentProps<typeof EditPrinterDialog>> = {},
) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        {children}
        <Toaster />
      </QueryClientProvider>
    );
  }

  const utils = render(
    <EditPrinterDialog
      printerId={PRINTER_ID}
      printerName="Test Printer"
      brand="Bambu"
      modelCode="P1S"
      serialNumber="SN-0001"
      currentIpAddress="10.0.0.50"
      machineProfileId={null}
      machineProfileName={null}
      processProfileId={null}
      processProfileName={null}
      open
      onOpenChange={onOpenChange}
      {...overrides}
    />,
    { wrapper: Wrapper },
  );
  return { ...utils, queryClient };
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('EditPrinterDialog', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('never renders brand/model/serial as editable controls', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({}))),
    );

    renderDialog();

    expect(screen.queryByLabelText(/brand/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/model/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/serial/i)).not.toBeInTheDocument();
  });

  it('saves a name-only change without calling verify-connection', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL, _init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('verify-connection')) {
        throw new Error(
          'verify-connection should not be called for a name-only edit',
        );
      }
      return Promise.resolve(
        jsonResponse({
          id: '11111111-1111-1111-1111-111111111111',
          name: 'Renamed Printer',
          ipAddress: '10.0.0.50',
        }),
      );
    });
    vi.stubGlobal('fetch', fetchMock);

    renderDialog();

    fireEvent.change(screen.getByLabelText('Name'), {
      target: { value: 'Renamed Printer' },
    });
    const saveButton = screen.getByRole('button', { name: 'Save' });
    expect(saveButton).not.toBeDisabled();
    fireEvent.click(saveButton);

    await waitFor(() => {
      expect(
        fetchMock.mock.calls.some((call) =>
          call[0]
            .toString()
            .includes('/api/v1/printers/11111111-1111-1111-1111-111111111111'),
        ),
      ).toBe(true);
    });

    const editCall = fetchMock.mock.calls.find((call) =>
      call[0]
        .toString()
        .includes('/api/v1/printers/11111111-1111-1111-1111-111111111111'),
    );
    expect(editCall).toBeDefined();
    const editRequestInit = editCall?.[1] as RequestInit;
    const requestBody = JSON.parse(editRequestInit.body as string);
    expect(requestBody).toEqual({
      name: 'Renamed Printer',
      machineProfileId: null,
      processProfileId: null,
    });
  });

  it('requires a successful verify before enabling Save when connection fields change', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('verify-connection')) {
        return Promise.resolve(jsonResponse({ verified: true }));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderDialog();

    const saveButton = screen.getByRole('button', { name: 'Save' });
    expect(saveButton).not.toBeDisabled();

    fireEvent.change(screen.getByLabelText('IP address'), {
      target: { value: '10.0.0.51' },
    });
    fireEvent.blur(screen.getByLabelText('IP address'));
    fireEvent.change(screen.getByLabelText('LAN access code'), {
      target: { value: '12345678' },
    });
    fireEvent.blur(screen.getByLabelText('LAN access code'));

    expect(saveButton).toBeDisabled();

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some((call) =>
          call[0].toString().includes('verify-connection'),
        ),
      ).toBe(true),
    );

    await waitFor(() => expect(saveButton).not.toBeDisabled());
  });

  it('disables the process profile picker until a machine profile is linked (FR-007)', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({}))),
    );

    renderDialog();

    expect(
      screen.getByRole('button', { name: 'Select a machine profile first' }),
    ).toBeDisabled();
  });

  it('enables the process profile picker once a machine profile is already linked', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({}))),
    );

    renderDialog(undefined, {
      machineProfileId: 'm1',
      machineProfileName: 'Bambu X1 Carbon 0.4 nozzle',
      processProfileId: null,
      processProfileName: null,
    });

    expect(
      screen.getByRole('button', { name: 'Bambu X1 Carbon 0.4 nozzle' }),
    ).not.toBeDisabled();
    expect(
      screen.getByRole('button', { name: 'No process profile linked' }),
    ).not.toBeDisabled();
  });

  it('selecting a machine profile from the picker clears any linked process profile locally', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/profiles/machine')) {
        return Promise.resolve(
          jsonResponse({
            items: [
              {
                id: 'm2',
                name: 'Another Machine',
                origin: 'User',
              },
            ],
          }),
        );
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderDialog(undefined, {
      machineProfileId: 'm1',
      machineProfileName: 'Bambu X1 Carbon 0.4 nozzle',
      processProfileId: 'p1',
      processProfileName: '0.20mm Standard',
    });

    expect(
      screen.getByRole('button', { name: '0.20mm Standard' }),
    ).not.toBeDisabled();

    await user.click(
      screen.getByRole('button', { name: 'Bambu X1 Carbon 0.4 nozzle' }),
    );
    await waitFor(() =>
      expect(screen.getByText('Another Machine')).toBeInTheDocument(),
    );
    await user.click(screen.getByText('Another Machine'));

    expect(
      screen.getByRole('button', { name: 'No process profile linked' }),
    ).toBeInTheDocument();
  });

  // Regression: linking a machine profile in this still-open, unsaved dialog used to leave the
  // process profile dropdown empty. The picker's process query used to be wired to `printerId`,
  // which the backend resolves against the printer's already-*persisted* MachineProfileId — not
  // whatever was just picked here — so a freshly-linked, not-yet-saved machine profile never made
  // it into the process query's compatibility filter.
  it('populates the process profile dropdown with a freshly-picked, unsaved machine profile', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/profiles/machine')) {
        return Promise.resolve(
          jsonResponse({
            items: [
              {
                id: 'm1',
                name: 'Bambu X1 Carbon 0.4 nozzle',
                origin: 'System',
              },
            ],
          }),
        );
      }
      if (url.includes('/profiles/process')) {
        const params = new URL(url, 'http://localhost').searchParams;
        expect(params.get('compatibleWithMachineName')).toBe(
          'Bambu X1 Carbon 0.4 nozzle',
        );
        expect(params.has('printerId')).toBe(false);
        return Promise.resolve(
          jsonResponse({
            items: [{ id: 'p1', name: '0.20mm Standard', origin: 'System' }],
          }),
        );
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderDialog();

    await user.click(
      screen.getByRole('button', { name: 'No machine profile linked' }),
    );
    await waitFor(() =>
      expect(
        screen.getByText('Bambu X1 Carbon 0.4 nozzle'),
      ).toBeInTheDocument(),
    );
    await user.click(screen.getByText('Bambu X1 Carbon 0.4 nozzle'));

    await user.click(
      screen.getByRole('button', { name: 'No process profile linked' }),
    );
    await waitFor(() =>
      expect(screen.getByText('0.20mm Standard')).toBeInTheDocument(),
    );
  });

  // Regression: a real live-browser walkthrough found that saving used to leave the printer detail
  // page and its activity feed showing stale data (the summary card kept reading "Not linked" right
  // after a successful save, only correcting itself on a manual reload) because the edit mutation's
  // onSuccess only invalidated the fleet-list query, never the detail page's own query or the
  // activity-log feed it also depends on.
  it('invalidates the printer detail and activity log queries on a successful save', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({}))),
    );

    const { queryClient } = renderDialog();
    const detailKey =
      getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey(
        PRINTER_ID,
      );
    const activityKey = ['/api/v1/activity-log'];
    queryClient.setQueryData(detailKey, { data: { id: PRINTER_ID } });
    queryClient.setQueryData(activityKey, { data: { items: [] } });

    fireEvent.change(screen.getByLabelText('Name'), {
      target: { value: 'Renamed Printer' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => {
      expect(queryClient.getQueryState(detailKey)?.isInvalidated).toBe(true);
    });
    expect(queryClient.getQueryState(activityKey)?.isInvalidated).toBe(true);
  });
});
