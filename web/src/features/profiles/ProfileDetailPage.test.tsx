import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { ProfileDetailPage } from './ProfileDetailPage';

const DETAIL = {
  id: 'fp1',
  name: 'Bambu PLA Basic',
  sourceFormat: 'OrcaSlicer',
  baseProfileName: 'Generic PLA',
  rawDataJson: '{"nozzle_temperature":["220"]}',
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function renderPage(putResponse: () => Response) {
  const puts: { url: string; body: unknown }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (init?.method?.toUpperCase() === 'PUT') {
        puts.push({ url, body: JSON.parse(init.body as string) });
        return Promise.resolve(putResponse());
      }
      if (url.endsWith('/profiles/filament/fp1')) {
        return Promise.resolve(json(DETAIL));
      }
      return Promise.resolve(json({ items: [] }));
    }),
  );
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const router = createMemoryRouter(
    [
      { path: '/profiles/:type/:id', element: <ProfileDetailPage /> },
      { path: '/profiles', element: <div>Profiles list</div> },
    ],
    { initialEntries: ['/profiles/filament/fp1'] },
  );
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
      <Toaster />
    </QueryClientProvider>,
  );
  return puts;
}

describe('ProfileDetailPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('loads the profile name, base and pretty-printed raw JSON', async () => {
    renderPage(() => json({}));

    expect(
      await screen.findByRole('heading', { name: 'Bambu PLA Basic' }),
    ).toBeInTheDocument();
    expect(screen.getByText(/Based on Generic PLA/)).toBeInTheDocument();
    expect(screen.getByRole('textbox')).toHaveValue(
      JSON.stringify({ nozzle_temperature: ['220'] }, null, 2),
    );
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
  });

  it('saves the edited raw data in one PUT', async () => {
    const user = userEvent.setup();
    const puts = renderPage(() => json({}));

    const textbox = await screen.findByRole('textbox');
    await user.clear(textbox);
    await user.paste('{"nozzle_temperature":["230"]}');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Saved.')).toBeInTheDocument();
    expect(puts).toEqual([
      {
        url: expect.stringMatching(/\/profiles\/filament\/fp1\/raw-data$/),
        body: { rawDataJson: '{"nozzle_temperature":["230"]}' },
      },
    ]);
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled(),
    );
  });

  it('shows the 422 problem detail inline and keeps the edited value', async () => {
    const user = userEvent.setup();
    renderPage(() =>
      json({ detail: 'Raw data is not valid JSON: line 1.' }, 422),
    );

    const textbox = await screen.findByRole('textbox');
    await user.clear(textbox);
    await user.paste('{not valid json');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Raw data is not valid JSON: line 1.',
    );
    expect(textbox).toHaveValue('{not valid json');
  });
});
