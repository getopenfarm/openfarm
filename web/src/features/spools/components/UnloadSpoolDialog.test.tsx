import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { UnloadSpoolDialog } from './UnloadSpoolDialog';

function stubFetch(onPut: (body: unknown) => void) {
  vi.stubGlobal(
    'fetch',
    vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'PUT') {
        onPut(JSON.parse(init.body as string));
      }
      return Promise.resolve(
        new Response(JSON.stringify({ id: 'spool-1' }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    }),
  );
}

function renderDialog(onPut: (body: unknown) => void) {
  stubFetch(onPut);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <UnloadSpoolDialog spoolId="spool-1" open onOpenChange={() => {}} />
    </QueryClientProvider>,
  );
}

describe('UnloadSpoolDialog', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns a spool to storage', async () => {
    const user = userEvent.setup();
    let putBody: unknown;
    renderDialog((body) => {
      putBody = body;
    });

    await user.click(screen.getByRole('button', { name: 'Return to storage' }));

    await waitFor(() => expect(putBody).toEqual({ action: 'ReturnToStorage' }));
  });

  it('leaves finishing to the Finish action', () => {
    renderDialog(() => {});

    expect(
      screen.queryByRole('button', { name: 'Mark finished' }),
    ).not.toBeInTheDocument();
  });
});
