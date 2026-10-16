import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { JobMatchingDialog } from './JobMatchingDialog';

function renderDialog() {
  return render(
    <MemoryRouter>
      <QueryClientProvider
        client={
          new QueryClient({ defaultOptions: { queries: { retry: false } } })
        }
      >
        <JobMatchingDialog jobId="job-1" />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

describe('JobMatchingDialog', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('fetches on demand, explains checks, refreshes and closes with Escape', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          jobId: 'job-1',
          evaluatedAtUtc: '2026-10-07T10:00:00Z',
          requirementSource: 'Job snapshot',
          outcome: 'Blocked',
          summary: 'P02 has a nozzle mismatch.',
          requirements: [
            {
              materialType: 'PETG',
              selectedVariantId: 'variant-1',
              selectedVariantName: 'PETG Basic · Black',
              selectedColorHex: '#27272a',
              detectedColorHex: '#000000',
            },
          ],
          jobChecks: [],
          printers: [
            {
              printerId: 'printer-1',
              printerName: 'P02',
              eligible: false,
              checks: [
                {
                  code: 'nozzle-match',
                  label: 'Nozzle diameter',
                  status: 'Failed',
                  required: '0.4 mm',
                  actual: '0.6 mm',
                  configurationUrl: '/printers/printer-1',
                },
              ],
            },
          ],
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );
    vi.stubGlobal('fetch', fetchMock);
    renderDialog();
    expect(fetchMock).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Job diagnosis' }));
    expect(
      await screen.findByRole('dialog', { name: 'Job diagnosis' }),
    ).toBeInTheDocument();
    expect(
      await screen.findByText('P02 has a nozzle mismatch.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Required: 0.4 mm')).toBeInTheDocument();
    expect(screen.getByText('Actual: 0.6 mm')).toBeInTheDocument();
    expect(
      screen.getByText(/Catalog color #27272a replaces file color #000000/),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('link', { name: 'Review configuration' }),
    ).toHaveAttribute('href', '/printers/printer-1');
    await user.click(screen.getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows an actionable error when diagnostics cannot be loaded', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response('{}', { status: 404 })),
    );
    renderDialog();
    await userEvent.click(
      screen.getByRole('button', { name: 'Job diagnosis' }),
    );
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Could not load matching diagnostics',
    );
  });
});
