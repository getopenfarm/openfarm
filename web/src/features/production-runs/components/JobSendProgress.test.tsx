import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { expect, it, vi } from 'vitest';
import type { PrintJobSendProgress } from '../hooks/sendProgress';
import { useProductionHub } from '../hooks/useProductionHub';
import { JobSendDetail, JobSendLabel } from './JobSendProgress';

const mock = vi.hoisted(() => ({
  handlers: new Map<string, (payload: unknown) => void>(),
}));
vi.mock('@/lib/signalr/farmHubConnection', () => ({
  connectFarmHub: () => ({
    connection: {
      on: (event: string, handler: (payload: unknown) => void) =>
        mock.handlers.set(event, handler),
    },
    stop: vi.fn(),
  }),
}));

function Row() {
  useProductionHub();
  return (
    <div>
      <JobSendLabel jobId="job-1" fallback="Dispatching…" />
      <JobSendDetail jobId="job-1" />
    </div>
  );
}

const push = (progress: Partial<PrintJobSendProgress>) =>
  act(() =>
    mock.handlers.get('PrintJobSendProgress')?.({
      printJobId: 'job-1',
      percent: null,
      attempt: 1,
      error: null,
      ...progress,
    }),
  );

it('shows each send stage pushed by the hub, then falls back when it ends', async () => {
  const client = new QueryClient();
  render(<Row />, {
    wrapper: ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  });
  expect(await screen.findByText('Dispatching…')).toBeInTheDocument();

  push({ stage: 'Slicing' });
  expect(await screen.findByText('Slicing…')).toBeInTheDocument();

  push({ stage: 'Uploading', percent: 40 });
  expect(await screen.findByText('Uploading 40%')).toBeInTheDocument();
  expect(screen.getByRole('progressbar')).toBeInTheDocument();

  push({ stage: 'Retrying', attempt: 2, error: 'MQTT connect canceled.' });
  expect(
    await screen.findByText('Send failed · retrying (attempt 2)'),
  ).toBeInTheDocument();
  expect(await screen.findByText('MQTT connect canceled.')).toBeInTheDocument();

  push({ stage: 'Uploading', percent: 80, attempt: 2 });
  expect(
    await screen.findByText('Uploading 80% · attempt 2'),
  ).toBeInTheDocument();

  push({ stage: 'Sent' });
  expect(
    await screen.findByText('Sent · waiting for printer'),
  ).toBeInTheDocument();

  push({ stage: 'Ended' });
  expect(await screen.findByText('Dispatching…')).toBeInTheDocument();
});
