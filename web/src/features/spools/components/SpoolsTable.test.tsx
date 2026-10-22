import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { SpoolResponse } from '@/lib/api/generated/endpoints.schemas';
import { SpoolsTable } from './SpoolsTable';

afterEach(() => vi.unstubAllGlobals());

function stubFetch() {
  const fetchMock = vi.fn().mockResolvedValue(
    new Response(JSON.stringify({ printers: [] }), {
      headers: { 'Content-Type': 'application/json' },
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function renderTable(spools: SpoolResponse[], selectable = false) {
  return render(
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
      <MemoryRouter>
        <SpoolsTable spools={spools} selectable={selectable} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

/** The PUTs sent, as [url, parsed body]. */
function puts(fetchMock: ReturnType<typeof vi.fn>) {
  return fetchMock.mock.calls
    .filter(([, init]) => init?.method === 'PUT')
    .map(([input, init]) => [
      String(input),
      init.body ? JSON.parse(init.body as string) : undefined,
    ]);
}

describe('SpoolsTable actions', () => {
  it('offers eligible actions in menus and restores focus after cancelling a dialog', async () => {
    const fetchMock = stubFetch();
    renderTable([
      { id: 'stored', label: 'Stored spool', state: 'InStorage' },
      { id: 'loaded', label: 'Loaded spool', state: 'Using' },
      { id: 'finished', label: 'Finished spool', state: 'Finished' },
    ]);
    expect(screen.getByRole('link', { name: 'Stored spool' })).toHaveAttribute(
      'href',
      '/spools/stored',
    );
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
    expect(screen.getByRole('menuitem', { name: 'Finish' })).toBeEnabled();
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
    expect(screen.getByRole('menuitem', { name: 'Finish' })).toBeEnabled();
    await userEvent.click(screen.getByRole('menuitem', { name: 'Unload' }));
    expect(screen.getByRole('dialog')).toHaveTextContent(
      'Unload spool from feed slot',
    );
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(loadedTrigger).toHaveFocus());
    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Finished spool' }),
    );
    expect(
      screen.getAllByRole('menuitem').map((item) => item.textContent),
    ).toEqual(['Restore']);
    await userEvent.keyboard('{Escape}');
    expect(puts(fetchMock)).toEqual([]);
  });

  it('finishes a spool with a quick reason, writing off what is left', async () => {
    const fetchMock = stubFetch();
    renderTable([
      {
        id: 'stored',
        label: 'Stored spool',
        state: 'InStorage',
        remainingWeightGrams: 340,
      },
    ]);

    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Stored spool' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Finish' }));
    const dialog = screen.getByRole('alertdialog');
    expect(dialog).toHaveTextContent('Finish Stored spool?');
    expect(dialog).toHaveTextContent('Writes off 340 g. Restore puts it back.');
    await userEvent.click(within(dialog).getByRole('radio', { name: 'Wet' }));
    expect(within(dialog).getByRole('textbox')).toHaveValue('Wet');
    await userEvent.click(
      within(dialog).getByRole('button', { name: 'Finish' }),
    );

    await waitFor(() =>
      expect(puts(fetchMock)).toEqual([
        [
          '/api/v1/spools/stored/unload',
          { action: 'MarkFinished', reason: 'Wet' },
        ],
      ]),
    );
    await waitFor(() =>
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument(),
    );
  });

  it('restores a finished spool from its menu', async () => {
    const fetchMock = stubFetch();
    renderTable([
      { id: 'finished', label: 'Finished spool', state: 'Finished' },
    ]);

    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Finished spool' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Restore' }));

    await waitFor(() =>
      expect(puts(fetchMock)).toEqual([
        ['/api/v1/spools/finished/restore', {}],
      ]),
    );
  });

  it('finishes the selected spools one after another', async () => {
    const fetchMock = stubFetch();
    renderTable(
      [
        {
          id: 'a',
          label: 'Spool A',
          state: 'InStorage',
          remainingWeightGrams: 100,
        },
        { id: 'b', label: 'Spool B', state: 'Using', remainingWeightGrams: 25 },
        {
          id: 'c',
          label: 'Spool C',
          state: 'InStorage',
          remainingWeightGrams: 900,
        },
      ],
      true,
    );

    await userEvent.click(
      screen.getByRole('checkbox', { name: 'Select Spool A' }),
    );
    await userEvent.click(
      screen.getByRole('checkbox', { name: 'Select Spool B' }),
    );
    expect(screen.getByRole('status')).toHaveTextContent('2 selected');
    await userEvent.click(
      screen.getByRole('button', { name: 'Finish selected' }),
    );
    const dialog = screen.getByRole('alertdialog');
    expect(dialog).toHaveTextContent('Finish 2 spools?');
    expect(dialog).toHaveTextContent('Writes off 125 g.');
    await userEvent.click(
      within(dialog).getByRole('button', { name: 'Finish' }),
    );

    await waitFor(() =>
      expect(puts(fetchMock)).toEqual([
        ['/api/v1/spools/a/unload', { action: 'MarkFinished', reason: null }],
        ['/api/v1/spools/b/unload', { action: 'MarkFinished', reason: null }],
      ]),
    );
  });

  it('offers no selection for finished spools', () => {
    stubFetch();
    renderTable([
      { id: 'finished', label: 'Finished spool', state: 'Finished' },
    ]);

    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
  });
});
