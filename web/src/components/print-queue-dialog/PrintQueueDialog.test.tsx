import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { jobsStore } from '@/features/jobs/data/mockJobsStore';
import { PrintQueueDialog, type PrintQueuePart } from './PrintQueueDialog';

const TEST_PART: PrintQueuePart = {
  name: 'Phone stand',
  estimatedTimeMinutes: 125,
  weightGrams: 58,
  primaryMaterial: { colorHex: '#3b82f6', label: 'PLA' },
};

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFleetFetch() {
  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/hubs/farm')) {
      return Promise.reject(new Error('SignalR not available in tests'));
    }
    return Promise.resolve(
      jsonResponse({
        printers: [
          {
            id: 'printer-1',
            name: 'X1C-01',
            brand: 'Bambu',
            modelCode: 'X1C',
            status: 'Idle',
            isReachable: true,
          },
          {
            id: 'printer-2',
            name: 'P1S-02',
            brand: 'Bambu',
            modelCode: 'P1S',
            status: 'Printing',
            isReachable: true,
          },
        ],
      }),
    );
  });
  vi.stubGlobal('fetch', fetchMock);
}

function renderDialog(part: PrintQueuePart = TEST_PART) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>{children}</MemoryRouter>
      </QueryClientProvider>
    );
  }

  return render(<PrintQueueDialog open onOpenChange={() => {}} part={part} />, {
    wrapper: Wrapper,
  });
}

describe('PrintQueueDialog', () => {
  afterEach(() => {
    jobsStore.reset();
    vi.unstubAllGlobals();
  });

  it('requires a printer selection before enabling submit', async () => {
    stubFleetFetch();
    const user = userEvent.setup();
    renderDialog();

    expect(screen.getByRole('button', { name: 'Add to queue' })).toBeDisabled();

    await user.click(screen.getByRole('combobox'));
    expect(await screen.findByText('X1C-01 (idle)')).toBeInTheDocument();
    expect(screen.getByText('P1S-02 (printing)')).toBeInTheDocument();
  });

  it('adds a job to the queue on submit', async () => {
    stubFleetFetch();
    const user = userEvent.setup();
    const beforeCount = jobsStore.getSnapshot().length;

    renderDialog();

    await user.click(screen.getByRole('combobox'));
    await user.click(await screen.findByText('X1C-01 (idle)'));
    await user.click(screen.getByRole('button', { name: 'Add to queue' }));

    const jobs = jobsStore.getSnapshot();
    expect(jobs.length).toBe(beforeCount + 1);
    const newJob = jobs[jobs.length - 1];
    expect(newJob.name).toBe('Phone stand ×1');
    expect(newJob.printerName).toBe('X1C-01');
    expect(newJob.status).toBe('queued');
    expect(newJob.priority).toBe('Normal');
  });

  it('scales the estimated queue time with quantity', async () => {
    stubFleetFetch();
    const user = userEvent.setup();
    renderDialog();

    expect(
      screen.getByText('Estimated queue time · 2h 5m'),
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Increase quantity' }));

    expect(
      screen.getByText('Estimated queue time · 4h 10m'),
    ).toBeInTheDocument();
  });
});
