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
