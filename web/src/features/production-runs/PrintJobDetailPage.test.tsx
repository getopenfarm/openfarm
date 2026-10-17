import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { PrintJobDetailPage } from './PrintJobDetailPage';

vi.mock('./hooks/useProductionHub', () => ({ useProductionHub: () => {} }));
afterEach(() => vi.unstubAllGlobals());

function setup(
  status: string,
  selectedVariantId?: string,
  snapshotState = 'Ready',
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
            ? { printers: [] }
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
  expect(
    screen.getByRole('link', { name: 'Back to print queue' }),
  ).toHaveFocus();
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
