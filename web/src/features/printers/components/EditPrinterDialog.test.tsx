import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { EditPrinterDialog } from './EditPrinterDialog';

function renderDialog(onOpenChange: (open: boolean) => void = () => {}) {
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

  return render(
    <EditPrinterDialog
      printerId="11111111-1111-1111-1111-111111111111"
      printerName="Test Printer"
      brand="Bambu"
      modelCode="P1S"
      serialNumber="SN-0001"
      currentIpAddress="10.0.0.50"
      open
      onOpenChange={onOpenChange}
    />,
    { wrapper: Wrapper },
  );
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
    const editRequestInit = editCall![1] as RequestInit;
    const requestBody = JSON.parse(editRequestInit.body as string);
    expect(requestBody).toEqual({ name: 'Renamed Printer' });
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
});
