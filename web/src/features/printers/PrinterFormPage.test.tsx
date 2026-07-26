import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { createMemoryRouter, Link, Outlet, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { PrinterFormPage } from './PrinterFormPage';

const PRINTER_ID = '11111111-1111-1111-1111-111111111111';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const CATALOG_RESPONSE = {
  brands: [
    { brand: 'Bambu', models: ['X1', 'X1C', 'P1S'] },
    { brand: 'System', models: ['Simulation'] },
  ],
};

function printerDetailBody(overrides?: Record<string, unknown>) {
  return {
    id: PRINTER_ID,
    name: 'Test Printer',
    brand: 'Bambu',
    modelCode: 'P1S',
    serialNumber: 'SN-0001',
    ipAddress: '10.0.0.50',
    status: 'Idle',
    isReachable: true,
    telemetry: {},
    amsUnits: [],
    machineProfileId: null,
    machineProfileName: null,
    processProfileId: null,
    processProfileName: null,
    ...overrides,
  };
}

function TestLayout() {
  return (
    <div>
      <Link to="/printers">Nav away</Link>
      <Outlet />
    </div>
  );
}

function renderRoute(initialPath: string) {
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

  const router = createMemoryRouter(
    [
      {
        element: <TestLayout />,
        children: [
          { path: '/printers/new', element: <PrinterFormPage mode="add" /> },
          {
            path: '/printers/:id/edit',
            element: <PrinterFormPage mode="edit" />,
          },
          { path: '/printers', element: <div>Printers list page</div> },
        ],
      },
    ],
    { initialEntries: [initialPath] },
  );

  render(<RouterProvider router={router} />, { wrapper: Wrapper });
}

describe('PrinterFormPage add mode', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('populates Brand/Model options from the fetched catalog', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/printers/catalog')) {
        return Promise.resolve(jsonResponse(CATALOG_RESPONSE));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute('/printers/new');

    await waitFor(() => {
      const brandSelect = screen.getByLabelText('Brand') as HTMLSelectElement;
      expect(Array.from(brandSelect.options).map((o) => o.value)).toEqual([
        'Bambu',
        'System',
      ]);
    });

    const modelSelect = screen.getByLabelText('Model') as HTMLSelectElement;
    expect(Array.from(modelSelect.options).map((o) => o.value)).toEqual([
      'X1',
      'X1C',
      'P1S',
    ]);
  });

  it('disables submission and shows an error state when the catalog fails to load', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/printers/catalog')) {
        return Promise.resolve(jsonResponse({ title: 'Server error' }, 500));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute('/printers/new');

    expect(
      await screen.findByText(/Couldn't load the printer catalog/),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add printer' })).toBeDisabled();
  });

  it('adds a printer once verification succeeds and navigates to /printers', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/printers/catalog')) {
        return Promise.resolve(jsonResponse(CATALOG_RESPONSE));
      }
      if (url.includes('verify-connection')) {
        return Promise.resolve(jsonResponse({ verified: true }));
      }
      if (url.endsWith('/api/v1/printers') && init?.method === 'POST') {
        return Promise.resolve(jsonResponse({ id: 'new-id' }, 201));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute('/printers/new');

    await waitFor(() =>
      expect(screen.getByLabelText('Brand')).not.toBeDisabled(),
    );

    await user.type(screen.getByLabelText('Name'), 'New Printer');
    await user.type(screen.getByLabelText('IP address'), '10.0.0.99');
    await user.type(screen.getByLabelText('Serial number'), 'SN-NEW-1');
    await user.type(screen.getByLabelText('LAN access code'), '12345678');
    await user.tab();

    await waitFor(() =>
      expect(screen.getByText('Connection verified.')).toBeInTheDocument(),
    );

    const submitButton = screen.getByRole('button', { name: 'Add printer' });
    await waitFor(() => expect(submitButton).not.toBeDisabled());
    await user.click(submitButton);

    expect(await screen.findByText('Printers list page')).toBeInTheDocument();
  });

  it('blocks in-app navigation away while the form has unsaved changes', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/printers/catalog')) {
        return Promise.resolve(jsonResponse(CATALOG_RESPONSE));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute('/printers/new');

    await waitFor(() =>
      expect(screen.getByLabelText('Brand')).not.toBeDisabled(),
    );
    await user.type(screen.getByLabelText('Name'), 'Dirty Printer');

    await user.click(screen.getByRole('link', { name: 'Nav away' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(
      within(dialog).getByText('Discard unsaved changes?'),
    ).toBeInTheDocument();
  });
});

describe('PrinterFormPage edit mode', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('never renders brand/model/serial as editable controls', async () => {
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes(`/api/v1/printers/${PRINTER_ID}`)) {
        return Promise.resolve(jsonResponse(printerDetailBody()));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute(`/printers/${PRINTER_ID}/edit`);

    await screen.findByLabelText('Name');
    expect(screen.queryByLabelText(/brand/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/model/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/serial/i)).not.toBeInTheDocument();
  });

  it('saves a name-only change without calling verify-connection, then navigates to /printers', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('verify-connection')) {
        throw new Error(
          'verify-connection should not be called for a name-only edit',
        );
      }
      if (
        url.includes(`/api/v1/printers/${PRINTER_ID}`) &&
        init?.method === 'PATCH'
      ) {
        return Promise.resolve(jsonResponse({ id: PRINTER_ID }));
      }
      if (url.includes(`/api/v1/printers/${PRINTER_ID}`)) {
        return Promise.resolve(jsonResponse(printerDetailBody()));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute(`/printers/${PRINTER_ID}/edit`);

    const nameInput = await screen.findByLabelText('Name');
    await user.clear(nameInput);
    await user.type(nameInput, 'Renamed Printer');

    const saveButton = screen.getByRole('button', { name: 'Save' });
    expect(saveButton).not.toBeDisabled();
    await user.click(saveButton);

    expect(await screen.findByText('Printers list page')).toBeInTheDocument();
  });

  it('requires a successful verify before enabling Save when connection fields change', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes(`/api/v1/printers/${PRINTER_ID}`)) {
        return Promise.resolve(jsonResponse(printerDetailBody()));
      }
      if (url.includes('verify-connection')) {
        return Promise.resolve(jsonResponse({ verified: true }));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute(`/printers/${PRINTER_ID}/edit`);

    const ipInput = await screen.findByLabelText('IP address');
    const saveButton = screen.getByRole('button', { name: 'Save' });
    expect(saveButton).not.toBeDisabled();

    await user.clear(ipInput);
    await user.type(ipInput, '10.0.0.51');
    fireEvent.blur(ipInput);
    const accessCodeInput = screen.getByLabelText('LAN access code');
    await user.type(accessCodeInput, '12345678');
    fireEvent.blur(accessCodeInput);

    expect(saveButton).toBeDisabled();

    await waitFor(() => expect(saveButton).not.toBeDisabled());
  });

  it('blocks in-app navigation away while the form has unsaved changes', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes(`/api/v1/printers/${PRINTER_ID}`)) {
        return Promise.resolve(jsonResponse(printerDetailBody()));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute(`/printers/${PRINTER_ID}/edit`);

    const nameInput = await screen.findByLabelText('Name');
    await user.clear(nameInput);
    await user.type(nameInput, 'Renamed Printer');

    await user.click(screen.getByRole('link', { name: 'Nav away' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(
      within(dialog).getByText('Discard unsaved changes?'),
    ).toBeInTheDocument();
  });
});
