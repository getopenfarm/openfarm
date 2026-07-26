import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { createMemoryRouter, Link, Outlet, RouterProvider } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { SpoolFormPage } from './SpoolFormPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const FILAMENTS_RESPONSE = {
  filaments: [
    {
      id: 'f-1',
      vendorName: 'Bambu Lab',
      material: 'PLA Basic',
      materialType: 'PLA',
      variants: [{ id: 'var-1', colorName: 'Black', colorHex: '#18181b' }],
    },
  ],
};

function TestLayout() {
  return (
    <div>
      <Link to="/spools">Nav away</Link>
      <Outlet />
    </div>
  );
}

function renderRoute(initialPath = '/spools/new') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        {children}
        <Toaster />
      </QueryClientProvider>
    );
  }

  const router = createMemoryRouter(
    [
      {
        element: <TestLayout />,
        children: [
          { path: '/spools/new', element: <SpoolFormPage /> },
          { path: '/spools', element: <div>Spools list page</div> },
        ],
      },
    ],
    { initialEntries: [initialPath] },
  );

  render(<RouterProvider router={router} />, { wrapper: Wrapper });
}

function stubFetch(onPost?: (url: string, body: unknown) => void) {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (init?.method === 'POST') {
        onPost?.(url, JSON.parse(init.body as string));
        return Promise.resolve(jsonResponse({ id: 'spool-new' }));
      }
      if (url.includes('/api/v1/filaments')) {
        return Promise.resolve(jsonResponse(FILAMENTS_RESPONSE));
      }
      return Promise.resolve(jsonResponse({}));
    }),
  );
}

describe('SpoolFormPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows a required-field error when submitted without a filament colour', async () => {
    const user = userEvent.setup();
    stubFetch();

    renderRoute();

    await user.click(screen.getByRole('button', { name: 'Add spool' }));

    expect(
      await screen.findByText('Choose a filament colour', { selector: 'p' }),
    ).toBeInTheDocument();
  });

  it('registers a spool for the selected filament variant, shows a toast, and navigates to /spools', async () => {
    const user = userEvent.setup();
    let postedBody: unknown;
    stubFetch((_url, body) => {
      postedBody = body;
    });

    renderRoute();

    await user.click(await screen.findByRole('combobox', { name: 'Filament' }));
    await user.click(
      await screen.findByRole('option', {
        name: 'Bambu Lab · PLA Basic · Black',
      }),
    );

    await user.click(screen.getByRole('button', { name: 'Add spool' }));

    await waitFor(() => {
      expect(postedBody).toMatchObject({
        filamentVariantId: 'var-1',
        spoolWeightGrams: 1000,
      });
    });

    expect(await screen.findByText('Spools list page')).toBeInTheDocument();
  });

  it('blocks in-app navigation away while the form has unsaved changes', async () => {
    const user = userEvent.setup();
    stubFetch();

    renderRoute();

    await user.click(await screen.findByRole('combobox', { name: 'Filament' }));
    await user.click(
      await screen.findByRole('option', {
        name: 'Bambu Lab · PLA Basic · Black',
      }),
    );

    await user.click(screen.getByRole('link', { name: 'Nav away' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(
      within(dialog).getByText('Discard unsaved changes?'),
    ).toBeInTheDocument();
  });
});
