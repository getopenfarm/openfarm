import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, expect, it, vi } from 'vitest';
import { BackfillManager } from './BackfillManager';

afterEach(() => vi.unstubAllGlobals());
it('shows demand without jobs and supports keyboard pause and confirmed removal', async () => {
  const requests: unknown[] = [];
  let removed = false;
  let paused = false;
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        const body = JSON.parse(String(init.body));
        requests.push(body);
        removed = body.action === 'Remove';
        paused = body.action === 'Pause';
      }
      const data = url.includes('/printers')
        ? { printers: [] }
        : removed
          ? []
          : [
              {
                id: 'd1',
                name: 'Bracket',
                state: paused ? 'Paused' : 'Enabled',
                printerCap: 1,
                targetKind: 'Part',
                targetId: 'part-1',
              },
            ];
      return new Response(JSON.stringify(data), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }),
  );
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <BackfillManager />
      </QueryClientProvider>
    </MemoryRouter>,
  );
  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Manage backfill' }));
  const actions = await screen.findByRole('button', {
    name: 'Actions for Bracket',
  });
  actions.focus();
  await user.keyboard('{Enter}');
  const pause = await screen.findByRole('menuitem', { name: 'Pause' });
  pause.focus();
  await user.keyboard('{Enter}');
  expect(await screen.findByText('Paused')).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Actions for Bracket' }));
  await user.click(screen.getByRole('menuitem', { name: 'Remove' }));
  expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Remove' }));
  await vi.waitFor(() =>
    expect(
      screen.queryByRole('link', { name: 'Bracket' }),
    ).not.toBeInTheDocument(),
  );
  expect(requests).toEqual([{ action: 'Pause' }, { action: 'Remove' }]);
  await vi.waitFor(() =>
    expect(screen.getByRole('button', { name: 'Add backfill' })).toHaveFocus(),
  );
});
