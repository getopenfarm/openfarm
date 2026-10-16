import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SpoolsTable } from './SpoolsTable';

afterEach(() => vi.unstubAllGlobals());

describe('SpoolsTable actions', () => {
  it('offers eligible actions in menus and restores focus after cancelling a dialog', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ printers: [] }), {
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);
    render(
      <QueryClientProvider
        client={
          new QueryClient({ defaultOptions: { queries: { retry: false } } })
        }
      >
        <MemoryRouter>
          <SpoolsTable
            spools={[
              { id: 'stored', label: 'Stored spool', state: 'InStorage' },
              { id: 'loaded', label: 'Loaded spool', state: 'Using' },
              { id: 'finished', label: 'Finished spool', state: 'Finished' },
            ]}
          />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    expect(screen.getByRole('link', { name: 'Stored spool' })).toHaveAttribute(
      'href',
      '/spools/stored',
    );
    expect(
      screen.queryByRole('button', { name: 'Actions for Finished spool' }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Load' }),
    ).not.toBeInTheDocument();
    const storedTrigger = screen.getByRole('button', {
      name: 'Actions for Stored spool',
    });
    await userEvent.click(storedTrigger);
    expect(
      screen.queryByRole('menuitem', { name: 'Record usage' }),
    ).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Load' }));
    expect(screen.getByRole('dialog')).toHaveTextContent('Load spool');
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(storedTrigger).toHaveFocus());
    const loadedTrigger = screen.getByRole('button', {
      name: 'Actions for Loaded spool',
    });
    await userEvent.click(loadedTrigger);
    expect(
      screen.queryByRole('menuitem', { name: 'Load' }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole('menuitem', { name: 'Record usage' }),
    ).toBeEnabled();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Unload' }));
    expect(screen.getByRole('dialog')).toHaveTextContent(
      'Unload spool from feed slot',
    );
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(loadedTrigger).toHaveFocus());
    expect(
      fetchMock.mock.calls.every(([, init]) => !init || init.method === 'GET'),
    ).toBe(true);
  });
});
