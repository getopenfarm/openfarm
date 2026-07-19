import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LoadSpoolDialog } from './LoadSpoolDialog';

function stubFetch(onPut: (url: string, body: unknown) => void) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (init?.method === 'PUT' && url.includes('/load')) {
        onPut(url, JSON.parse(init.body as string));
        return Promise.resolve(
          new Response(JSON.stringify({ id: 'spool-1', state: 'Using' }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
      }
      if (url.includes('/api/v1/printers') && url.includes('/feed-units')) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              feedUnits: [
                {
                  id: 'fu-1',
                  kind: 'MultiMaterialUnit',
                  displayLabel: 'AMS 1',
                  isRevealed: true,
                  feedSlots: [
                    {
                      id: 'slot-1',
                      slotPosition: 0,
                      displayLabel: 'Slot 1',
                      currentSpool: null,
                    },
                    {
                      id: 'slot-2',
                      slotPosition: 1,
                      displayLabel: 'Slot 2',
                      currentSpool: { id: 'other-spool' },
                    },
                  ],
                },
              ],
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          ),
        );
      }
      if (url.includes('/api/v1/printers')) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              printers: [{ id: 'printer-1', name: 'X1C-01' }],
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          ),
        );
      }
      return Promise.resolve(
        new Response(JSON.stringify({}), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    }),
  );
}

function renderDialog(onPut: (url: string, body: unknown) => void) {
  stubFetch(onPut);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <LoadSpoolDialog spoolId="spool-1" open onOpenChange={() => {}} />
    </QueryClientProvider>,
  );
}

describe('LoadSpoolDialog', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('loads a spool onto the chosen printer/Feed Slot, offering only free slots', async () => {
    const user = userEvent.setup();
    let putBody: unknown;
    renderDialog((_url, body) => {
      putBody = body;
    });

    await user.click(await screen.findByRole('combobox', { name: 'Printer' }));
    await user.click(await screen.findByRole('option', { name: 'X1C-01' }));

    await user.click(
      await screen.findByRole('combobox', { name: 'Feed Slot' }),
    );
    expect(
      await screen.findByRole('option', { name: 'AMS 1 · Slot 1' }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('option', { name: 'AMS 1 · Slot 2' }),
    ).not.toBeInTheDocument();
    await user.click(screen.getByRole('option', { name: 'AMS 1 · Slot 1' }));

    await user.click(screen.getByRole('button', { name: 'Load spool' }));

    await waitFor(() => expect(putBody).toEqual({ feedSlotId: 'slot-1' }));
  });
});
