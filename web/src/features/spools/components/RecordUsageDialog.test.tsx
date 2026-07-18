import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { RecordUsageDialog } from './RecordUsageDialog';

function stubFetch(response: unknown, onPost: (body: unknown) => void) {
  vi.stubGlobal(
    'fetch',
    vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'POST') {
        onPost(JSON.parse(init.body as string));
        return Promise.resolve(
          new Response(JSON.stringify(response), {
            status: 200,
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

function renderDialog(response: unknown, onPost: (body: unknown) => void) {
  stubFetch(response, onPost);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <RecordUsageDialog spoolId="spool-1" open onOpenChange={() => {}} />
    </QueryClientProvider>,
  );
}

describe('RecordUsageDialog', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('records grams consumed against the spool', async () => {
    const user = userEvent.setup();
    let postedBody: unknown;
    renderDialog(
      {
        id: 'spool-1',
        state: 'Using',
        remainingWeightGrams: 100,
        hasUsageOverrun: false,
        autoContinuation: { occurred: false },
      },
      (body) => {
        postedBody = body;
      },
    );

    await user.type(screen.getByLabelText('Grams consumed'), '400');
    await user.click(screen.getByRole('button', { name: 'Record usage' }));

    await waitFor(() => expect(postedBody).toEqual({ gramsConsumed: 400 }));
  });
});
