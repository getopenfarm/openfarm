import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CreateVendorDialog } from './CreateVendorDialog';

function stubFetch(onPost: (body: unknown) => void) {
  vi.stubGlobal(
    'fetch',
    vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'POST') {
        onPost(JSON.parse(init.body as string));
        return Promise.resolve(
          new Response(JSON.stringify({ id: 'v-new', name: 'New Vendor Co' }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          }),
        );
      }
      return Promise.resolve(
        new Response(JSON.stringify({ vendors: [] }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    }),
  );
}

function renderDialog(
  onCreated?: (vendor: { id?: string; name?: string }) => void,
) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <CreateVendorDialog onCreated={onCreated} />
    </QueryClientProvider>,
  );
}

describe('CreateVendorDialog', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('creates a vendor and reports it back via onCreated', async () => {
    const user = userEvent.setup();
    let postedBody: unknown;
    const onCreated = vi.fn();
    stubFetch((body) => {
      postedBody = body;
    });
    renderDialog(onCreated);

    await user.click(screen.getByRole('button', { name: 'Add vendor' }));
    await user.type(screen.getByLabelText('Name'), 'New Vendor Co');
    await user.click(screen.getByRole('button', { name: 'Add vendor' }));

    await waitFor(() => expect(postedBody).toEqual({ name: 'New Vendor Co' }));
    await waitFor(() =>
      expect(onCreated).toHaveBeenCalledWith(
        expect.objectContaining({ id: 'v-new', name: 'New Vendor Co' }),
      ),
    );
  });
});
