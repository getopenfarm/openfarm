import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { toast } from 'sonner';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { PartSummaryResponse } from '@/lib/api/generated/endpoints.schemas';
import { PartsTable } from './PartsTable';

const part: PartSummaryResponse = {
  id: 'part-1',
  name: 'Bracket',
  fileFormat: 'GCode',
  printRequirementsStatus: 'Complete',
  estimateStatus: 'Unavailable',
};

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function setup(
  options: {
    parts?: PartSummaryResponse[];
    failure?: boolean;
    detailStatus?: string;
  } = {},
) {
  const requests = vi.fn(
    async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (init?.method === 'POST') {
        return options.failure
          ? jsonResponse(
              { detail: 'The part no longer has complete print requirements.' },
              422,
            )
          : jsonResponse({ id: 'run-1' }, 201);
      }
      if (url.includes('/api/v1/parts/part-1')) {
        return jsonResponse({
          ...part,
          unitsPerPrint: 4,
          printRequirementsStatus: options.detailStatus ?? 'Complete',
        });
      }
      throw new Error(`Unexpected fetch: ${url}`);
    },
  );
  vi.stubGlobal('fetch', requests);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const onRowClick = vi.fn();
  render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <PartsTable parts={options.parts ?? [part]} onRowClick={onRowClick} />
      </QueryClientProvider>
    </MemoryRouter>,
  );
  return { requests, queryClient, onRowClick };
}

async function openQueue() {
  await userEvent.click(
    screen.getByRole('button', { name: 'Actions for Bracket' }),
  );
  await userEvent.click(
    screen.getByRole('menuitem', { name: 'Send to queue' }),
  );
}

describe('PartsTable queue action', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('queues the selected part with a chosen quantity and refreshes queue/run lists', async () => {
    const user = userEvent.setup();
    const success = vi.spyOn(toast, 'success');
    const { requests, queryClient, onRowClick } = setup();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    await openQueue();
    expect(onRowClick).not.toHaveBeenCalled();
    const input = await screen.findByLabelText('Quantity (units)');
    expect(input).toHaveValue(4);
    expect(
      screen.getByText(/1 print job · 4 units will be produced/),
    ).toBeInTheDocument();
    fireEvent.change(input, { target: { value: '5' } });
    expect(
      screen.getByText(/2 print jobs · 8 units will be produced/),
    ).toBeInTheDocument();
    await user.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Send to queue',
      }),
    );
    await waitFor(() =>
      expect(requests).toHaveBeenCalledWith(
        expect.stringContaining('/api/v1/production-runs'),
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({
            lines: [
              {
                targetKind: 'Part',
                targetId: 'part-1',
                quantity: 5,
                assignmentMode: 'Unassigned',
                revisionMode: 'Snapshot',
              },
            ],
          }),
        }),
      ),
    );
    await waitFor(() =>
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument(),
    );
    expect(invalidate).toHaveBeenCalledWith({
      queryKey: ['/api/v1/print-jobs'],
    });
    expect(invalidate).toHaveBeenCalledWith({
      queryKey: ['/api/v1/production-runs'],
    });
    expect(success).toHaveBeenCalledWith(
      'Bracket sent to the queue.',
      expect.objectContaining({
        action: expect.objectContaining({ label: 'View queue' }),
      }),
    );
  });

  it('cancels without creating jobs', async () => {
    const user = userEvent.setup();
    const { requests } = setup();
    await openQueue();
    await user.click(await screen.findByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(
      requests.mock.calls.some(([, init]) => init?.method === 'POST'),
    ).toBe(false);
  });

  it('disables incomplete parts while allowing parts without estimates', async () => {
    setup({
      parts: [
        part,
        {
          ...part,
          id: 'part-2',
          name: 'Incomplete part',
          printRequirementsStatus: 'Incomplete',
        },
      ],
    });
    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Bracket' }),
    );
    expect(
      screen.getByRole('menuitem', { name: 'Send to queue' }),
    ).not.toHaveAttribute('aria-disabled');
    await userEvent.keyboard('{Escape}');
    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Incomplete part' }),
    );
    expect(
      screen.getByRole('menuitem', { name: 'Send to queue' }),
    ).toHaveAttribute('aria-disabled', 'true');
    expect(
      screen.getByRole('menuitem', { name: 'Send to queue' }),
    ).toHaveAttribute(
      'title',
      'Complete this part’s print requirements before queueing.',
    );
  });

  it('rechecks requirements after opening a stale table row', async () => {
    setup({ detailStatus: 'Incomplete' });
    await openQueue();
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Complete this part’s print requirements before queueing.',
    );
    expect(screen.queryByLabelText('Quantity (units)')).not.toBeInTheDocument();
  });

  it('shows server errors and preserves the quantity for retry', async () => {
    const user = userEvent.setup();
    const { queryClient } = setup({ failure: true });
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    await openQueue();
    const input = await screen.findByLabelText('Quantity (units)');
    fireEvent.change(input, { target: { value: '9' } });
    await user.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Send to queue',
      }),
    );
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The part no longer has complete print requirements.',
    );
    expect(input).toHaveValue(9);
    expect(invalidate).not.toHaveBeenCalled();
  });

  it('rejects a fractional number of units without creating jobs', async () => {
    const { requests } = setup();
    await openQueue();
    fireEvent.change(await screen.findByLabelText('Quantity (units)'), {
      target: { value: '1.5' },
    });
    const form = screen.getByLabelText('Quantity (units)').closest('form');
    if (!form) throw new Error('Queue form was not rendered.');
    fireEvent.submit(form);
    expect(
      await screen.findByText('Enter a whole number of units.'),
    ).toBeInTheDocument();
    expect(
      requests.mock.calls.some(([, init]) => init?.method === 'POST'),
    ).toBe(false);
  });
});
