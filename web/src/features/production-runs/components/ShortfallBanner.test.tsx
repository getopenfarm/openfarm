import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router';
import { beforeEach, expect, it, vi } from 'vitest';
import { ShortfallBanner } from './ShortfallBanner';

const mutation = vi.hoisted(() => ({ mutateAsync: vi.fn(), isPending: false }));
vi.mock('@/lib/api/generated/print-jobs/print-jobs', () => ({
  useOpenFarmApiEndpointsPrintJobsRequeuePrintJobEndpoint: () => mutation,
}));
beforeEach(() => {
  mutation.mutateAsync.mockReset().mockResolvedValue({});
  mutation.isPending = false;
});

function setup(ready: string[], blocked: string[]) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter>
        <Routes>
          <Route
            path="/"
            element={
              <ShortfallBanner
                runId="run-1"
                requirement={{
                  partName: 'Bracket',
                  jobsNeedingRequeue: ready,
                  jobsBlockedForRequeue: blocked,
                }}
              />
            }
          />
          <Route path="/jobs/:id" element={<p>Job history</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

it('keeps blocked shortfalls visible with keyboard-accessible history links', async () => {
  const user = userEvent.setup();
  setup([], ['missing-job']);
  expect(screen.getByText(/cannot be requeued/)).toBeInTheDocument();
  expect(
    screen.queryByRole('button', { name: /Requeue job/ }),
  ).not.toBeInTheDocument();
  const link = screen.getByRole('link', { name: 'View job missing-' });
  expect(link).toHaveAttribute('href', '/jobs/missing-job');
  await user.tab();
  expect(link).toHaveFocus();
  await user.keyboard('{Enter}');
  expect(screen.getByText('Job history')).toBeInTheDocument();
  expect(mutation.mutateAsync).not.toHaveBeenCalled();
});

it('offers requeue only for verified snapshot jobs in a mixed shortfall', async () => {
  const user = userEvent.setup();
  setup(['ready-job'], ['missing-job']);
  const action = screen.getByRole('button', { name: /Requeue job ready-jo$/ });
  expect(screen.getAllByRole('button')).toHaveLength(1);
  await user.tab();
  expect(action).toHaveFocus();
  await user.keyboard('{Enter}');
  expect(mutation.mutateAsync).toHaveBeenCalledExactlyOnceWith({
    id: 'ready-job',
  });
  expect(
    screen.getByRole('link', { name: 'View job missing-' }),
  ).toBeInTheDocument();
});

it('prevents repeated requeue submissions while pending', () => {
  mutation.isPending = true;
  setup(['ready-job'], []);
  expect(
    screen.getByRole('button', { name: /Requeue job ready-jo$/ }),
  ).toBeDisabled();
});
