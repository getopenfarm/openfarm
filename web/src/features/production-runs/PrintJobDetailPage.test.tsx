import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { PrintJobDetailPage } from './PrintJobDetailPage';

vi.mock('./hooks/useProductionHub', () => ({ useProductionHub: () => {} }));
afterEach(() => vi.unstubAllGlobals());

function setup(status: string, selectedVariantId?: string) {
  const requests = vi.fn(
    async (input: RequestInfo | URL, _init?: RequestInit) => {
      const url = input.toString();
      const job = {
        id: 'job-1',
        status,
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

it('saves per-job requirements before dispatch', async () => {
  const requests = setup('Queued');
  const materials = await screen.findAllByLabelText('Material');
  fireEvent.change(materials[0], { target: { value: 'ABS' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save filament' }));
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

it('can split usage across spools for each required material', async () => {
  const requests = setup('Failed');
  fireEvent.click(
    await screen.findByRole('button', { name: 'Add spool for PETG' }),
  );
  const materials = screen.getAllByLabelText('Material') as HTMLInputElement[];
  expect(materials[2].value).toBe('PETG');
  const grams = screen.getAllByLabelText('Grams consumed');
  fireEvent.change(grams[1], { target: { value: '12.5' } });
  fireEvent.change(grams[2], { target: { value: '3' } });
  fireEvent.click(screen.getByRole('button', { name: 'Save filament' }));
  await waitFor(() =>
    expect(requests).toHaveBeenCalledWith(
      expect.stringContaining('/filament-consumption'),
      expect.objectContaining({
        method: 'POST',
        body: expect.stringContaining('12.5'),
      }),
    ),
  );
});

it('preserves a saved catalog choice and can explicitly clear it on a queued job', async () => {
  const requests = setup('Queued', 'variant-1');
  await waitFor(() =>
    expect(screen.getAllByRole('combobox')[0]).toHaveTextContent(
      'Acme PLA Basic · Black',
    ),
  );
  fireEvent.click(screen.getByRole('button', { name: 'Save filament' }));
  await waitFor(() => {
    const put = requests.mock.calls.find(([, init]) => init?.method === 'PUT');
    expect(
      JSON.parse(String(put?.[1]?.body)).requirements[0]
        .narrowFilamentVariantId,
    ).toBe('variant-1');
  });
  const user = userEvent.setup();
  await user.click(screen.getAllByRole('combobox')[0]);
  await user.click(
    screen.getByRole('option', { name: 'Use file material and colour' }),
  );
  fireEvent.click(screen.getByRole('button', { name: 'Save filament' }));
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
