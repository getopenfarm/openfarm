import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AddFeedUnitDialog } from './AddFeedUnitDialog';

function stubFetch(onPost: (body: unknown) => void) {
  vi.stubGlobal(
    'fetch',
    vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'POST') {
        onPost(JSON.parse(init.body as string));
        return Promise.resolve(
          new Response(JSON.stringify({ id: 'fu-new' }), {
            status: 201,
            headers: { 'Content-Type': 'application/json' },
          }),
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

function renderDialog(onPost: (body: unknown) => void) {
  stubFetch(onPost);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <AddFeedUnitDialog printerId="printer-1" open onOpenChange={() => {}} />
    </QueryClientProvider>,
  );
}

describe('AddFeedUnitDialog', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('adds an Automatic Filament Reloader with a name and slot count', async () => {
    const user = userEvent.setup();
    let postedBody: unknown;
    renderDialog((body) => {
      postedBody = body;
    });

    await user.type(screen.getByLabelText('Name'), 'Splicer');
    await user.click(screen.getByRole('button', { name: 'Add Feed Unit' }));

    await waitFor(() =>
      expect(postedBody).toEqual({
        kind: 'AutomaticFilamentReloader',
        name: 'Splicer',
        slotCount: 2,
        startingSlotPosition: 0,
        replacesFeedUnitId: null,
        transferExternalSpool: true,
      }),
    );
  });

  it('defaults to replacing a hidden external port and transferring its spool', async () => {
    const user = userEvent.setup();
    let posted: unknown;
    const fetchMock = vi.fn((_url: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'POST') posted = JSON.parse(init.body as string);
      return Promise.resolve(
        new Response(
          JSON.stringify({
            feedUnits: [
              {
                id: 'external',
                kind: 'ExternalSpool',
                feedSlots: [
                  { id: 'external-slot', currentSpool: { id: 'spool' } },
                ],
              },
            ],
          }),
          {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          },
        ),
      );
    });
    vi.stubGlobal('fetch', fetchMock);
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    render(
      <QueryClientProvider client={queryClient}>
        <AddFeedUnitDialog printerId="printer-1" open onOpenChange={() => {}} />
      </QueryClientProvider>,
    );
    expect(
      await screen.findByLabelText('Feeds through external spool port'),
    ).toBeChecked();
    expect(
      screen.getByLabelText('Move external spool to the feeding channel'),
    ).toBeChecked();
    await user.type(screen.getByLabelText('Name'), 'Reloader');
    await user.click(screen.getByRole('button', { name: 'Add Feed Unit' }));
    await waitFor(() =>
      expect(posted).toMatchObject({
        replacesFeedUnitId: 'external',
        startingSlotPosition: 0,
        transferExternalSpool: true,
      }),
    );
  });

  it('configures an existing reloader and selects its second channel', async () => {
    const user = userEvent.setup();
    let submitted: unknown;
    vi.stubGlobal(
      'fetch',
      vi.fn((_url: RequestInfo | URL, init?: RequestInit) => {
        if (init?.method === 'PUT') {
          submitted = JSON.parse(init.body as string);
          return Promise.resolve(new Response(null, { status: 204 }));
        }
        return Promise.resolve(
          new Response(
            JSON.stringify({
              feedUnits: [{ id: 'external', kind: 'ExternalSpool' }],
            }),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          ),
        );
      }),
    );
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    render(
      <QueryClientProvider client={client}>
        <AddFeedUnitDialog
          printerId="p1"
          open
          onOpenChange={() => {}}
          unit={{
            id: 'afr',
            kind: 'AutomaticFilamentReloader',
            displayLabel: 'Reloader',
            replacesFeedUnitId: 'external',
            feedingSlotId: 'ch1',
            feedSlots: [
              { id: 'ch1', slotPosition: 0 },
              { id: 'ch2', slotPosition: 1 },
            ],
          }}
        />
      </QueryClientProvider>,
    );
    await screen.findByLabelText('Feeds through external spool port');
    await user.click(screen.getByRole('combobox', { name: 'Feeding channel' }));
    await user.click(screen.getByRole('option', { name: 'Channel 2' }));
    await user.click(
      screen.getByRole('button', { name: 'Save Configuration' }),
    );
    await waitFor(() =>
      expect(submitted).toMatchObject({
        replacesFeedUnitId: 'external',
        feedingSlotId: 'ch2',
      }),
    );
  });

  it('reveals an External Spool with no extra fields', async () => {
    const user = userEvent.setup();
    let postedBody: unknown;
    renderDialog((body) => {
      postedBody = body;
    });

    await user.click(screen.getByRole('combobox', { name: 'Kind' }));
    await user.click(screen.getByRole('option', { name: 'External Spool' }));
    await user.click(screen.getByRole('button', { name: 'Add Feed Unit' }));

    await waitFor(() => expect(postedBody).toEqual({ kind: 'ExternalSpool' }));
  });
});
