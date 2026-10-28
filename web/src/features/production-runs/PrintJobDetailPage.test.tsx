import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { clearToken, setToken } from '@/lib/auth/tokenStorage';
import { PrintJobDetailPage } from './PrintJobDetailPage';

vi.mock('./hooks/useProductionHub', () => ({ useProductionHub: () => {} }));
afterEach(() => {
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
  clearToken();
});

function setup(
  status: string,
  selectedVariantId?: string,
  snapshotState = 'Ready',
  extra: Record<string, unknown> = {},
  printers: unknown[] = [],
) {
  const requests = vi.fn(
    async (input: RequestInfo | URL, _init?: RequestInit) => {
      const url = input.toString();
      const job = {
        id: 'job-1',
        status,
        snapshotState,
        snapshotRecoveryReason:
          snapshotState === 'Unavailable'
            ? 'Source part removed; retry and file download are unavailable.'
            : null,
        plateQuantity: 1,
        fileRevisionNumber: 1,
        ...extra,
        filamentConsumption: [
          {
            materialType: 'PLA',
            detectedColorHex: null,
            gramsConsumed: null,
            narrowFilamentVariantId: selectedVariantId,
          },
          {
            materialType: 'PETG',
            detectedColorHex: '000000',
            gramsConsumed: null,
          },
        ],
      };
      const body = url.includes('/filaments')
        ? {
            filaments: [
              {
                id: 'filament-1',
                materialType: 'PLA',
                material: 'PLA Basic',
                vendorName: 'Acme',
                variants: [
                  { id: 'variant-1', colorName: 'Black', colorHex: '#27272a' },
                ],
              },
            ],
          }
        : url.includes('/spools')
          ? { spools: [] }
          : url.includes('/printers')
            ? { printers }
            : job;
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    },
  );
  vi.stubGlobal('fetch', requests);
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
      <MemoryRouter initialEntries={['/jobs/job-1']}>
        <Routes>
          <Route path="/jobs/:id" element={<PrintJobDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return requests;
}

it('saves per-job requirements from the Edit filament dialog', async () => {
  const requests = setup('Queued');
  expect(
    screen.queryByRole('button', { name: 'Record usage' }),
  ).not.toBeInTheDocument();
  fireEvent.click(await screen.findByRole('button', { name: 'Edit filament' }));
  const materials = await screen.findAllByLabelText('Material');
  fireEvent.change(materials[0], { target: { value: 'ABS' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() =>
    expect(requests).toHaveBeenCalledWith(
      expect.stringContaining('/filament-requirements'),
      expect.objectContaining({
        method: 'PUT',
        body: expect.stringContaining('ABS'),
      }),
    ),
  );
});

it('shows consumption read-only once the job is terminal', async () => {
  setup('Failed');
  expect(await screen.findByText('Filament consumed')).toBeInTheDocument();
  expect(screen.getAllByText('Not deducted')).toHaveLength(2);
  expect(
    screen.queryByRole('button', { name: 'Edit filament' }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole('button', { name: 'Record usage' }),
  ).not.toBeInTheDocument();
});

it('preserves a saved catalog choice and can explicitly clear it on a queued job', async () => {
  const requests = setup('Queued', 'variant-1');
  const user = userEvent.setup();
  await user.click(
    await screen.findByRole('button', { name: 'Edit filament' }),
  );
  await waitFor(() =>
    expect(screen.getAllByRole('combobox')[0]).toHaveTextContent(
      'Acme PLA Basic · Black',
    ),
  );
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => {
    const put = requests.mock.calls.find(([, init]) => init?.method === 'PUT');
    expect(
      JSON.parse(String(put?.[1]?.body)).requirements[0]
        .narrowFilamentVariantId,
    ).toBe('variant-1');
  });
  await waitFor(() =>
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
  );
  await user.click(screen.getByRole('button', { name: 'Edit filament' }));
  await user.click(screen.getAllByRole('combobox')[0]);
  await user.click(
    screen.getByRole('option', { name: 'Use file material and color' }),
  );
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => {
    const puts = requests.mock.calls.filter(
      ([, init]) => init?.method === 'PUT',
    );
    expect(
      JSON.parse(String(puts.at(-1)?.[1]?.body)).requirements[0]
        .narrowFilamentVariantId,
    ).toBeNull();
  });
});

it('disables download and explains permanently removed legacy sources', async () => {
  const user = userEvent.setup();
  const requests = setup('Cancelled', undefined, 'Unavailable');
  const download = await screen.findByRole('button', {
    name: 'Download print file',
  });
  expect(download).toBeDisabled();
  expect(download).toHaveAccessibleDescription(/Source part removed/);
  expect(screen.getByText('Job source removed')).toBeInTheDocument();
  await user.tab();
  expect(screen.getByRole('link', { name: 'Back to queue' })).toHaveFocus();
  await user.tab();
  expect(download).not.toHaveFocus();
  await user.click(download);
  expect(
    requests.mock.calls.some(([input]) => input.toString().endsWith('/file')),
  ).toBe(false);
});

it('keeps download available for cancelled jobs with verified snapshots', async () => {
  setup('Cancelled');
  expect(
    await screen.findByRole('button', { name: 'Download print file' }),
  ).toBeEnabled();
  expect(screen.queryByText('Job source removed')).not.toBeInTheDocument();
});

it('disables download while a legacy snapshot remains unresolved', async () => {
  setup('Cancelled', undefined, 'Unresolved');
  expect(
    await screen.findByRole('button', { name: 'Download print file' }),
  ).toBeDisabled();
  expect(screen.getByText('Job snapshot needs recovery')).toBeInTheDocument();
});

it('shows a printer-started print without the job-only controls', async () => {
  const external = {
    id: 'job-1',
    source: 'External',
    name: 'benchy.gcode.3mf',
    status: 'Completed',
    snapshotState: 'Ready',
    dispatchedPrinterId: 'printer-1',
    startedAtUtc: '2026-10-18T07:00:00Z',
    endedAtUtc: '2026-10-18T08:00:00Z',
    actualDurationMinutes: 60,
    filamentConsumption: [
      {
        materialType: 'PLA',
        detectedColorHex: '000000',
        estimatedGrams: 20,
        gramsConsumed: 20,
      },
    ],
  };
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const body = input.toString().includes('/printers')
        ? { printers: [{ id: 'printer-1', name: 'Bambu A1', brand: 'Bambu' }] }
        : external;
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }),
  );
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={['/jobs/job-1']}>
        <Routes>
          <Route path="/jobs/:id" element={<PrintJobDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );

  expect(await screen.findByText('benchy.gcode.3mf')).toBeInTheDocument();
  expect(
    screen.getByText('Started on the printer, not from OpenFarm'),
  ).toBeInTheDocument();
  expect(await screen.findByRole('link', { name: 'Bambu A1' })).toHaveAttribute(
    'href',
    '/printers/printer-1',
  );
  expect(screen.getByText('60 min')).toBeInTheDocument();
  expect(screen.getByText('20 g')).toBeInTheDocument();
  expect(
    screen.queryByRole('button', { name: 'Download print file' }),
  ).not.toBeInTheDocument();
  expect(
    screen.queryByRole('button', { name: 'Job diagnosis' }),
  ).not.toBeInTheDocument();
});

it.each([undefined, '', 'https://farm.example/'])(
  'downloads authenticated files using API base %s',
  async (base) => {
    vi.stubEnv('VITE_API_BASE_URL', base);
    setToken('download-token');
    const requests = setup('Cancelled');
    const button = await screen.findByRole('button', {
      name: 'Download print file',
    });
    // Exercise the actual request without invoking browser-only blob navigation.
    requests.mockImplementation(
      async () => new Response(null, { status: 404 }),
    );
    await userEvent.setup().click(button);
    await waitFor(() =>
      expect(requests).toHaveBeenCalledWith(
        `${base ? 'https://farm.example' : ''}/api/v1/print-jobs/job-1/file`,
        { headers: { Authorization: 'Bearer download-token' } },
      ),
    );
  },
);

const BLOCK_REASON =
  "Process profile '0.20mm Standard @BBL X1C' is not in Bambu Studio 2.7.1. Select another profile, or pin the part to another slicer.";

it('shows why the last dispatch attempt failed on a queued job', async () => {
  setup('Queued', undefined, 'Ready', { lastBlockReason: BLOCK_REASON });

  expect(await screen.findByText('Last attempt failed')).toBeInTheDocument();
  expect(screen.getByText(BLOCK_REASON)).toBeInTheDocument();
});

it('offers manual status on a printer whose model has no driver', async () => {
  setup('Queued', undefined, 'Ready', { dispatchedPrinterId: 'printer-1' }, [
    { id: 'printer-1', name: 'P01', model: null },
  ]);

  expect(
    await screen.findByRole('button', { name: 'Mark started' }),
  ).toBeInTheDocument();
});

it('does not show a stale block reason once the job is no longer queued', async () => {
  setup('Printing', undefined, 'Ready', { lastBlockReason: BLOCK_REASON });

  await screen.findByRole('button', { name: 'Job diagnosis' });
  expect(screen.queryByText('Last attempt failed')).not.toBeInTheDocument();
});

it('shows the slicer and the profiles a job was sliced with', async () => {
  setup('Printing', undefined, 'Ready', {
    provenance: {
      engine: 'BambuStudio',
      version: '2.7.1',
      presets: [
        {
          kind: 'Process',
          selectedName: '0.20mm Standard @BBL X1C',
          resolvedName: '0.20mm Standard @BBL X1C',
          outcome: 'MappedEngine',
        },
        {
          kind: 'Filament',
          selectedName: 'Bambu PLA Basic @BBL X1C',
          resolvedName: 'Bambu PLA Basic @BBL X1C v2',
          outcome: 'MappedVersion',
        },
      ],
    },
  });

  expect(
    await screen.findByText('Sliced with Bambu Studio 2.7.1'),
  ).toBeInTheDocument();
  expect(
    screen.getByText(
      'Process: 0.20mm Standard @BBL X1C — the slicer’s own copy of the selected profile',
    ),
  ).toBeInTheDocument();
  expect(
    screen.getByText(
      'Filament: Bambu PLA Basic @BBL X1C v2 (selected: Bambu PLA Basic @BBL X1C)',
    ),
  ).toBeInTheDocument();
  expect(
    screen.getByRole('button', { name: 'Download print file' }),
  ).toHaveAttribute(
    'title',
    'The file is made again with the current settings. It can differ from the sent file.',
  );
});

it('shows no slicing provenance for a job that was never sliced', async () => {
  setup('Queued');

  await screen.findByRole('button', { name: 'Job diagnosis' });
  expect(screen.queryByText(/^Sliced with/)).not.toBeInTheDocument();
  expect(
    screen.getByRole('button', { name: 'Download print file' }),
  ).not.toHaveAttribute('title');
});

/** A closed print whose filament lines carry what the server recorded about price and cost. */
function renderCostedJob(
  lines: Record<string, unknown>[],
  status = 'Completed',
) {
  const job = {
    id: 'job-1',
    status,
    snapshotState: 'Ready',
    plateQuantity: 1,
    fileRevisionNumber: 1,
    filamentConsumption: lines,
  };
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = input.toString();
      const body = url.includes('/printers')
        ? { printers: [] }
        : url.includes('/spools')
          ? { spools: [] }
          : url.includes('/filaments')
            ? { filaments: [] }
            : job;
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }),
  );
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={['/jobs/job-1']}>
        <Routes>
          <Route path="/jobs/:id" element={<PrintJobDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const SPOOL_LINE = {
  materialType: 'PLA',
  detectedColorHex: '000000',
  gramsConsumed: 50,
  pricePerKg: 20,
  priceSource: 'Spool',
  cost: 1,
  spoolId: 'spool-1',
};
const COLOR_LINE = {
  materialType: 'PETG',
  detectedColorHex: 'FFFFFF',
  gramsConsumed: 30,
  pricePerKg: 30,
  priceSource: 'Variant',
  cost: 0.9,
  spoolId: 'spool-2',
};

it('shows each deducted line with its cost, price source and a link to its spool, and a total', async () => {
  renderCostedJob([SPOOL_LINE, COLOR_LINE]);

  expect(await screen.findByText('Filament consumed')).toBeInTheDocument();
  expect(screen.getByText('50 g')).toBeInTheDocument();
  expect(screen.getByText('1.00')).toBeInTheDocument();
  expect(screen.getByText('20.00 per kg · spool price')).toBeInTheDocument();
  expect(screen.getByText('30.00 per kg · color price')).toBeInTheDocument();
  const links = screen.getAllByRole('link', { name: 'Spool' });
  expect(links.map((link) => link.getAttribute('href'))).toEqual([
    '/filament/spools/spool-1',
    '/filament/spools/spool-2',
  ]);
  expect(screen.getByText('Filament cost')).toBeInTheDocument();
  expect(screen.getByText('1.90')).toBeInTheDocument();
  expect(screen.queryByText('Partial')).not.toBeInTheDocument();
});

it('marks the total partial when a line has no price or was not deducted, without counting it as zero', async () => {
  renderCostedJob(
    [
      SPOOL_LINE,
      {
        materialType: 'ABS',
        detectedColorHex: null,
        gramsConsumed: 10,
        pricePerKg: null,
        cost: null,
        spoolId: 'spool-3',
      },
      { materialType: 'TPU', detectedColorHex: null, gramsConsumed: null },
    ],
    'Failed',
  );

  expect(await screen.findByText('Filament cost')).toBeInTheDocument();
  expect(screen.getByText('Partial')).toBeInTheDocument();
  expect(screen.getByText('No price')).toBeInTheDocument();
  expect(screen.getByText('Not deducted')).toBeInTheDocument();
  // Only the priced line is added: the unpriced 10 g is not costed as free.
  expect(screen.getAllByText('1.00')).toHaveLength(2);
});

it('shows no total when no line has a cost', async () => {
  renderCostedJob([
    {
      materialType: 'PLA',
      detectedColorHex: null,
      gramsConsumed: 20,
      cost: null,
    },
  ]);

  expect(await screen.findByText('No price')).toBeInTheDocument();
  expect(screen.queryByText('Filament cost')).not.toBeInTheDocument();
});

it('shows a spool priced at zero as a cost of 0.00, not as no price', async () => {
  renderCostedJob([
    { ...SPOOL_LINE, pricePerKg: 0, cost: 0, gramsConsumed: 50 },
  ]);

  expect(
    await screen.findByText('0.00 per kg · spool price'),
  ).toBeInTheDocument();
  expect(screen.getAllByText('0.00')).toHaveLength(2);
  expect(screen.queryByText('No price')).not.toBeInTheDocument();
  expect(screen.queryByText('Partial')).not.toBeInTheDocument();
});

it('shows no cost while the print is still running', async () => {
  renderCostedJob(
    [{ materialType: 'PLA', detectedColorHex: null, estimatedGrams: 20 }],
    'Printing',
  );

  expect(await screen.findByText('Filament requirements')).toBeInTheDocument();
  expect(screen.getByText('Est. 20 g')).toBeInTheDocument();
  expect(screen.queryByText('Filament cost')).not.toBeInTheDocument();
});
