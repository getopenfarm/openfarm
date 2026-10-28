import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SendToQueueDialog } from './SendToQueueDialog';

vi.mock('../hooks/useProductionHub', () => ({
  invalidateProductionQueries: () => {},
}));

function setup(node: ReactNode) {
  const posts: { url: string; body: unknown }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (init?.method === 'POST') {
        posts.push({ url, body: JSON.parse(String(init.body)) });
        return new Response(JSON.stringify({ id: 'run-7' }), { status: 200 });
      }
      const body = url.includes('/api/v1/production-runs')
        ? { items: [{ id: 'run-7', displayLabel: 'Order #7' }], totalCount: 1 }
        : url.includes('/explosion')
          ? { printedParts: [{ partId: 'part-1', quantity: 2 }] }
          : {
              id: 'part-1',
              name: 'Base',
              unitsPerPrint: 2,
              printRequirementsStatus: 'Complete',
              estimatedPrintTimeMinutes: 60,
            };
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }),
  );
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>{node}</QueryClientProvider>
    </MemoryRouter>,
  );
  return posts;
}

describe('SendToQueueDialog', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('adds an assembly to an open run, saying what the sets turn into', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    const posts = setup(
      <SendToQueueDialog
        target={{ kind: 'Assembly', id: 'lamp', name: 'Lamp' }}
        onClose={onClose}
      />,
    );

    const quantity = await screen.findByLabelText('Quantity (sets)');
    await user.clear(quantity);
    await user.type(quantity, '3');
    // 3 lamps × 2 bases = 6 units at 2 per plate.
    expect(await screen.findByText('3 plates · ≈ 3h')).toBeInTheDocument();
    await user.click(screen.getByRole('combobox', { name: 'Run' }));
    await user.click(await screen.findByRole('option', { name: 'Order #7' }));
    await user.click(screen.getByRole('button', { name: 'Send to queue' }));

    await vi.waitFor(() => expect(onClose).toHaveBeenCalled());
    expect(posts).toEqual([
      {
        url: expect.stringContaining('/api/v1/production-runs/run-7/lines'),
        body: {
          lines: [
            {
              targetKind: 'Assembly',
              targetId: 'lamp',
              quantity: 3,
              assignmentMode: 'Unassigned',
            },
          ],
        },
      },
    ]);
  });
});
