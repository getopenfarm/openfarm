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

const CATALOG_WITH_SNAPMAKER = {
  brands: [...CATALOG_RESPONSE.brands, { brand: 'Snapmaker', models: ['U1'] }],
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
    buildPlateType: null,
    buildPlateTypeLabel: null,
    compatibleBuildPlateTypes: [
      { value: 'TexturedPei', label: 'Textured PEI Plate' },
      { value: 'HighTemp', label: 'High Temp Plate' },
    ],
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

async function openManualEntry() {
  fireEvent.click(await screen.findByRole('button', { name: 'Add by IP' }));
}

const DISCOVERED = [
  {
    serialNumber: '01P00A123456789',
    name: 'P1S-Bay3',
    brand: 'Bambu',
    modelCode: 'P1S',
    rawModel: 'C12',
    ipAddress: '192.168.1.42',
    lastSeenAt: '2026-10-10T00:00:00Z',
    alreadyRegistered: false,
  },
  {
    serialNumber: '01S00C000000001',
    name: 'X1C-01',
    brand: 'Bambu',
    modelCode: 'X1C',
    rawModel: 'BL-P001',
    ipAddress: '192.168.1.40',
    lastSeenAt: '2026-10-10T00:00:00Z',
    alreadyRegistered: true,
  },
];

describe('PrinterFormPage add mode — discovery', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('asks only for the access code, verifies, adds, and returns to the scan list', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/printers/discovered')) {
        return Promise.resolve(jsonResponse(DISCOVERED));
      }
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

    await user.click(
      await screen.findByRole('button', { name: 'Select P1S-Bay3' }),
    );

    expect(screen.getByLabelText('Name')).toHaveValue('P1S-Bay3');
    expect(screen.queryByLabelText('IP address')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Serial number')).not.toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(screen.getByText(/01P00A123456789/)).toBeInTheDocument();

    await user.type(screen.getByLabelText('Access code'), '12345678');
    await user.click(screen.getByRole('button', { name: 'Add printer' }));

    expect(await screen.findByText('Printer added.')).toBeInTheDocument();
    expect(
      await screen.findByRole('button', { name: 'Select P1S-Bay3' }),
    ).toBeInTheDocument();
    const registerCall = fetchMock.mock.calls.find(
      ([input, init]) =>
        String(input).endsWith('/api/v1/printers') && init?.method === 'POST',
    );
    expect(JSON.parse(String(registerCall?.[1]?.body))).toMatchObject({
      serialNumber: '01P00A123456789',
      ipAddress: '192.168.1.42',
      modelCode: 'P1S',
      accessCode: '12345678',
    });
  });

  it('does not add the printer when the connection check fails', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/printers/discovered')) {
        return Promise.resolve(jsonResponse(DISCOVERED));
      }
      if (url.includes('/printers/catalog')) {
        return Promise.resolve(jsonResponse(CATALOG_RESPONSE));
      }
      if (url.includes('verify-connection')) {
        return Promise.resolve(jsonResponse({ title: 'Unreachable' }, 422));
      }
      if (init?.method === 'POST') {
        return Promise.resolve(jsonResponse({ id: 'new-id' }, 201));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute('/printers/new');

    await user.click(
      await screen.findByRole('button', { name: 'Select P1S-Bay3' }),
    );
    await user.type(screen.getByLabelText('Access code'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Add printer' }));

    expect(await screen.findByText(/Connection failed/)).toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(
        ([input, init]) =>
          String(input).endsWith('/api/v1/printers') && init?.method === 'POST',
      ),
    ).toBe(false);
  });

  it('shows model pickers when discovery reports an unknown model', async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/printers/catalog')) {
          return Promise.resolve(jsonResponse(CATALOG_RESPONSE));
        }
        return Promise.resolve(
          jsonResponse([{ ...DISCOVERED[0], modelCode: null, rawModel: 'N9' }]),
        );
      }),
    );

    renderRoute('/printers/new');

    await user.click(
      await screen.findByRole('button', { name: 'Select P1S-Bay3' }),
    );
    expect(screen.getByRole('combobox', { name: 'Model' })).toBeInTheDocument();
    expect(screen.queryByLabelText('IP address')).not.toBeInTheDocument();
  });

  it('marks already-registered printers instead of offering Select', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse(DISCOVERED))),
    );

    renderRoute('/printers/new');

    expect(await screen.findByText('Added')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Select X1C-01' }),
    ).not.toBeInTheDocument();
  });

  it('offers manual entry when nothing is found', async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        return Promise.resolve(
          jsonResponse(
            url.includes('/printers/catalog') ? CATALOG_RESPONSE : [],
          ),
        );
      }),
    );

    renderRoute('/printers/new');

    expect(await screen.findByText('No printers found')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add by IP' }));
    expect(await screen.findByLabelText('Serial number')).toBeInTheDocument();
  });

  it('adds a discovered Snapmaker with no API key, checking the discovered SN', async () => {
    const user = userEvent.setup();
    const bodies: Record<string, unknown>[] = [];
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/printers/discovered')) {
        return Promise.resolve(
          jsonResponse([
            {
              serialNumber: 'SNU1TEST0001',
              name: 'Bay 4',
              brand: 'Snapmaker',
              modelCode: 'U1',
              rawModel: 'Snapmaker U1',
              ipAddress: '10.0.0.9',
              lastSeenAt: '2026-10-24T00:00:00Z',
              alreadyRegistered: false,
            },
          ]),
        );
      }
      if (url.includes('/printers/catalog')) {
        return Promise.resolve(jsonResponse(CATALOG_WITH_SNAPMAKER));
      }
      if (url.includes('verify-connection')) {
        bodies.push(JSON.parse(String(init?.body)));
        // The first attempt fails, to show the Snapmaker error.
        return Promise.resolve(
          bodies.length === 1
            ? jsonResponse({ title: 'Unreachable' }, 422)
            : jsonResponse({ verified: true }),
        );
      }
      if (url.endsWith('/api/v1/printers') && init?.method === 'POST') {
        bodies.push(JSON.parse(String(init?.body)));
        return Promise.resolve(jsonResponse({ id: 'new-id' }, 201));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute('/printers/new');

    expect(
      await screen.findByText('Snapmaker U1 · 10.0.0.9'),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Select Bay 4' }));
    expect(screen.queryByLabelText('IP address')).not.toBeInTheDocument();
    expect(screen.getByLabelText('API key')).toHaveValue('');

    await user.click(screen.getByRole('button', { name: 'Add printer' }));
    expect(
      await screen.findByText('Connection failed. Check API key.'),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add printer' }));

    expect(await screen.findByText('Printer added.')).toBeInTheDocument();
    expect(bodies).toHaveLength(3);
    for (const body of bodies)
      expect(body).toMatchObject({
        brand: 'Snapmaker',
        modelCode: 'U1',
        ipAddress: '10.0.0.9',
        serialNumber: 'SNU1TEST0001',
        accessCode: '',
      });
  });
});

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
    await openManualEntry();

    const user = userEvent.setup();
    await waitFor(() =>
      expect(
        screen.getByRole('combobox', { name: 'Brand' }),
      ).not.toBeDisabled(),
    );
    expect(screen.getByRole('combobox', { name: 'Brand' })).toHaveTextContent(
      'Bambu',
    );
    expect(screen.getByRole('combobox', { name: 'Model' })).toHaveTextContent(
      'X1',
    );
    await user.click(screen.getByRole('combobox', { name: 'Brand' }));
    expect(
      screen.getAllByRole('option').map((option) => option.textContent),
    ).toEqual(['Bambu', 'System']);
    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('combobox', { name: 'Model' }));
    expect(
      screen.getAllByRole('option').map((option) => option.textContent),
    ).toEqual(['X1', 'X1C', 'P1S']);
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
    await openManualEntry();

    expect(
      await screen.findByText(/Could not load printer catalog/),
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
    await openManualEntry();

    await waitFor(() =>
      expect(screen.getByLabelText('Brand')).not.toBeDisabled(),
    );

    await user.type(screen.getByLabelText('Name'), 'New Printer');
    await user.type(screen.getByLabelText('IP address'), '10.0.0.99');
    await user.type(screen.getByLabelText('Serial number'), 'SN-NEW-1');
    await user.type(screen.getByLabelText('Access code'), '12345678');
    await user.click(screen.getByRole('button', { name: 'Add printer' }));

    expect(await screen.findByText('Printers list page')).toBeInTheDocument();
  });

  it('adds a Snapmaker by IP alone: no serial field, optional API key', async () => {
    const user = userEvent.setup();
    const bodies: Record<string, unknown>[] = [];
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/printers/catalog')) {
        return Promise.resolve(jsonResponse(CATALOG_WITH_SNAPMAKER));
      }
      if (url.includes('verify-connection')) {
        bodies.push(JSON.parse(String(init?.body)));
        return Promise.resolve(jsonResponse({ verified: true }));
      }
      if (url.endsWith('/api/v1/printers') && init?.method === 'POST') {
        bodies.push(JSON.parse(String(init?.body)));
        return Promise.resolve(jsonResponse({ id: 'new-id' }, 201));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute('/printers/new');
    await openManualEntry();
    await waitFor(() =>
      expect(
        screen.getByRole('combobox', { name: 'Brand' }),
      ).not.toBeDisabled(),
    );
    await user.click(screen.getByRole('combobox', { name: 'Brand' }));
    await user.click(screen.getByRole('option', { name: 'Snapmaker' }));

    expect(screen.queryByLabelText('Serial number')).not.toBeInTheDocument();
    expect(screen.queryByText(/LAN mode disconnects/)).not.toBeInTheDocument();
    await user.type(screen.getByLabelText('Name'), 'U1-Bay1');
    await user.type(screen.getByLabelText('IP address'), '10.0.0.77');
    expect(screen.getByLabelText('API key')).toHaveValue('');
    await user.click(screen.getByRole('button', { name: 'Add printer' }));

    expect(await screen.findByText('Printers list page')).toBeInTheDocument();
    expect(bodies).toHaveLength(2);
    for (const body of bodies)
      expect(body).toMatchObject({
        brand: 'Snapmaker',
        modelCode: 'U1',
        ipAddress: '10.0.0.77',
        serialNumber: '',
        accessCode: '',
      });
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
    await openManualEntry();

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
    const verifyBodies: Record<string, unknown>[] = [];
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes(`/api/v1/printers/${PRINTER_ID}`)) {
        return Promise.resolve(jsonResponse(printerDetailBody()));
      }
      if (url.includes('verify-connection')) {
        verifyBodies.push(JSON.parse(String(init?.body)));
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
    const accessCodeInput = screen.getByLabelText('Access code');
    await user.type(accessCodeInput, '12345678');
    fireEvent.blur(accessCodeInput);

    expect(saveButton).toBeDisabled();

    await waitFor(() => expect(saveButton).not.toBeDisabled());
    // The endpoint requires the brand and model to pick the printer's connector.
    expect(verifyBodies.at(-1)).toMatchObject({
      brand: 'Bambu',
      modelCode: 'P1S',
      ipAddress: '10.0.0.51',
      serialNumber: 'SN-0001',
      accessCode: '12345678',
    });
  });

  it('moves a Snapmaker without an API key: verifies the new IP with none', async () => {
    const user = userEvent.setup();
    const verifyBodies: Record<string, unknown>[] = [];
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes(`/api/v1/printers/${PRINTER_ID}`)) {
        return Promise.resolve(
          jsonResponse(
            printerDetailBody({
              brand: 'Snapmaker',
              modelCode: 'U1',
              serialNumber: 'U1SN0001',
            }),
          ),
        );
      }
      if (url.includes('verify-connection')) {
        verifyBodies.push(JSON.parse(String(init?.body)));
        return Promise.resolve(jsonResponse({ verified: true }));
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute(`/printers/${PRINTER_ID}/edit`);

    const ipInput = await screen.findByLabelText('IP address');
    const keyInput = screen.getByLabelText('API key');
    expect(keyInput).toHaveAttribute('placeholder', 'Blank = keep current');
    await user.clear(ipInput);
    await user.type(ipInput, '10.0.0.52');
    fireEvent.blur(ipInput);

    expect(keyInput).toHaveAttribute('placeholder', 'Blank = none');
    const saveButton = screen.getByRole('button', { name: 'Save' });
    await waitFor(() => expect(saveButton).not.toBeDisabled());
    expect(verifyBodies).toEqual([
      {
        brand: 'Snapmaker',
        modelCode: 'U1',
        ipAddress: '10.0.0.52',
        serialNumber: 'U1SN0001',
        accessCode: '',
      },
    ]);
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

  it('blocks navigation when only a picker outside the form changed', async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes(`/api/v1/printers/${PRINTER_ID}`)) {
          return Promise.resolve(jsonResponse(printerDetailBody()));
        }
        return Promise.resolve(jsonResponse({}));
      }),
    );

    renderRoute(`/printers/${PRINTER_ID}/edit`);

    await screen.findByLabelText('Name');
    await user.click(screen.getByRole('combobox'));
    await user.click(
      await screen.findByRole('option', { name: 'High Temp Plate' }),
    );
    await user.click(screen.getByRole('link', { name: 'Nav away' }));

    expect(await screen.findByRole('alertdialog')).toHaveTextContent(
      'Discard unsaved changes?',
    );
  });

  // --- 012 US1: build plate <Select> (test-after, plan.md's TDD scoping) ---

  it('renders the backend-supplied compatible build plate labels, not a hand-maintained list', async () => {
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

    await screen.findByLabelText('Name');
    await user.click(screen.getByRole('combobox'));

    expect(
      await screen.findByRole('option', { name: 'Textured PEI Plate' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('option', { name: 'High Temp Plate' }),
    ).toBeInTheDocument();
  });

  it('submits the selected build plate enum value on Save, with no machine-profile prerequisite gate', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (
        url.includes(`/api/v1/printers/${PRINTER_ID}`) &&
        init?.method === 'PATCH'
      ) {
        return Promise.resolve(jsonResponse({ id: PRINTER_ID }));
      }
      if (url.includes(`/api/v1/printers/${PRINTER_ID}`)) {
        return Promise.resolve(
          jsonResponse(
            printerDetailBody({
              machineProfileId: null,
              machineProfileName: null,
            }),
          ),
        );
      }
      return Promise.resolve(jsonResponse({}));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderRoute(`/printers/${PRINTER_ID}/edit`);

    await screen.findByLabelText('Name');
    const saveButton = screen.getByRole('button', { name: 'Save' });
    expect(saveButton).not.toBeDisabled();

    await user.click(screen.getByRole('combobox'));
    await user.click(
      await screen.findByRole('option', { name: 'Textured PEI Plate' }),
    );
    await user.click(saveButton);

    let patchCalls: typeof fetchMock.mock.calls = [];
    await waitFor(() => {
      patchCalls = fetchMock.mock.calls.filter(
        ([, init]) => (init as RequestInit | undefined)?.method === 'PATCH',
      );
      expect(patchCalls).toHaveLength(1);
    });

    const [, patchInit] = patchCalls[0];
    const body = JSON.parse((patchInit as RequestInit).body as string);
    expect(body.buildPlateType).toBe('TexturedPei');
  });
});
