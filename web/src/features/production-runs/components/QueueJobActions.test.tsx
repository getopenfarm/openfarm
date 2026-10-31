import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { QueueJobRowResponse } from '@/lib/api/generated/endpoints.schemas';
import { QueueJobActions, type RemoteAction } from './QueueJobActions';

function renderActions(job: QueueJobRowResponse, onMoveToTop = vi.fn()) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
  function Harness() {
    const [pending, setPending] = useState<RemoteAction>(null);
    return (
      <>
        {pending && <span>{pending} requested</span>}
        <QueueJobActions
          job={job}
          printers={[{ id: 'p1', name: 'P01' }]}
          onMoveToTop={onMoveToTop}
          pending={pending}
          onPendingChange={setPending}
        />
      </>
    );
  }
  render(
    <QueryClientProvider client={queryClient}>
      <Harness />
    </QueryClientProvider>,
  );
  return { invalidate, onMoveToTop };
}
function stub(status = 200) {
  const fetchMock = vi.fn((_input: RequestInfo | URL, _options?: RequestInit) =>
    Promise.resolve(
      new Response(
        JSON.stringify(
          status === 200
            ? { status: 'Cancelled' }
            : { detail: 'Printer unavailable' },
        ),
        { status, headers: { 'Content-Type': 'application/json' } },
      ),
    ),
  );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}
const base = { jobId: 'j1', partName: 'Bracket', status: 'Queued' };
afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('QueueJobActions', () => {
  it('offers keyboard-accessible move and unpin actions for queued jobs', async () => {
    const fetchMock = stub();
    const { onMoveToTop, invalidate } = renderActions({
      ...base,
      assignmentMode: 'Pinned',
      pinnedPrinterId: 'p1',
    });
    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Bracket' }),
    );
    await userEvent.click(
      screen.getByRole('menuitem', { name: 'Move to top' }),
    );
    expect(onMoveToTop).toHaveBeenCalledOnce();
    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Bracket' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Unpin' }));
    await waitFor(() => expect(invalidate).toHaveBeenCalled());
    expect(fetchMock.mock.calls[0][0].toString()).toContain('/j1/unpin');
  });

  it('requires confirmation before removing a job', async () => {
    const fetchMock = stub();
    renderActions(base);
    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Bracket' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Remove job' }));
    expect(screen.getByText('Remove this job?')).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Remove job' }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledOnce());
    expect(fetchMock.mock.calls[0][0].toString()).toContain('/j1/cancel');
  });

  it('shows pause pending after the menu closes and prevents repeat requests', async () => {
    stub();
    renderActions({ ...base, status: 'Printing', dispatchedPrinterId: 'p1' });
    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Bracket' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Pause' }));
    expect(await screen.findByText('pause requested')).toBeInTheDocument();
    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Bracket' }),
    );
    expect(
      screen.getByRole('menuitem', { name: 'Pause requested…' }),
    ).toHaveAttribute('aria-disabled', 'true');
    expect(
      screen.queryByRole('menuitem', { name: 'Remove job' }),
    ).not.toBeInTheDocument();
  });

  it('clears pending feedback when a remote request fails', async () => {
    stub(409);
    renderActions({ ...base, status: 'Paused' });
    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Bracket' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Resume' }));
    await waitFor(() =>
      expect(screen.queryByText('resume requested')).not.toBeInTheDocument(),
    );
  });
});

it('requeues an eligible direct plate through the keyboard without a Run', async () => {
  const fetchMock = stub();
  const { invalidate } = renderActions({
    ...base,
    status: 'Failed',
    productionRunId: null,
    canRequeue: true,
  });
  const user = userEvent.setup();
  const trigger = screen.getByRole('button', { name: 'Actions for Bracket' });
  trigger.focus();
  await user.keyboard('{Enter}');
  const item = await screen.findByRole('menuitem', { name: 'Requeue plate' });
  item.focus();
  await user.keyboard('{Enter}');
  await waitFor(() => expect(fetchMock).toHaveBeenCalledOnce());
  expect(fetchMock.mock.calls[0][0].toString()).toContain('/j1/requeue');
  await waitFor(() => expect(invalidate).toHaveBeenCalled());
});
