vi.mock('@/features/parts/hooks/usePartsHub', () => ({
  usePartsHub: () => {},
}));

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { toast } from 'sonner';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PreferencesProvider } from '@/lib/preferences/usePreferences';
import { AssembliesPage } from './AssembliesPage';

function jsonResponse(body: unknown, status = 200) {
  return new Response(
    JSON.stringify(
      body && typeof body === 'object' && 'items' in body
        ? {
            ...body,
            ancestors: [],
            items: (
              body as { items: Array<{ id: string; name: string }> }
            ).items.map((assembly) => ({
              id: assembly.id,
              name: assembly.name,
              kind: 'assembly',
              assembly,
            })),
          }
        : body,
    ),
    {
      status,
      headers: { 'Content-Type': 'application/json' },
    },
  );
}

function stubFetch(routes: Record<string, unknown>) {
  const sortedRoutes = Object.entries(routes).sort(
    ([a], [b]) => b.length - a.length,
  );
  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/folders'))
      return Promise.resolve(
        jsonResponse({ items: [], totalCount: 0, ancestors: [] }),
      );
    for (const [path, body] of sortedRoutes) {
      if (
        url
          .replace('/api/v1/library/assemblies/contents', '/api/v1/assemblies')
          .includes(path)
      ) {
        return Promise.resolve(jsonResponse(body));
      }
    }
    return Promise.reject(new Error(`Unexpected fetch: ${url}`));
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>{children}</MemoryRouter>
      </QueryClientProvider>
    );
  }

  return render(
    <PreferencesProvider>
      <AssembliesPage />
    </PreferencesProvider>,
    { wrapper: Wrapper },
  );
}

describe('AssembliesPage', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows an empty state before any assembly exists', async () => {
    stubFetch({ '/api/v1/assemblies': { items: [], totalCount: 0 } });

    renderPage();

    expect(await screen.findByText('No assemblies')).toBeInTheDocument();
  });

  it('renders assemblies fetched from the real API', async () => {
    stubFetch({
      '/api/v1/assemblies': {
        items: [
          { id: 'a1', name: 'Enclosure', description: null, lineCount: 3 },
        ],
        totalCount: 1,
      },
    });

    renderPage();

    expect(await screen.findByText('Enclosure')).toBeInTheDocument();
    expect(screen.getByText('3 lines')).toBeInTheDocument();
  });

  it('bulk-removes selected assemblies, retrying ones a parent was blocking', async () => {
    const success = vi.spyOn(toast, 'success');
    const error = vi.spyOn(toast, 'error');
    let childAttempts = 0;
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = input.toString();
      if (url.includes('/folders'))
        return Promise.resolve(
          jsonResponse({ items: [], totalCount: 0, ancestors: [] }),
        );
      if (init?.method !== 'DELETE')
        return Promise.resolve(
          jsonResponse({
            items: [
              { id: 'child', name: 'Hinge', lineCount: 1 },
              { id: 'parent', name: 'Enclosure', lineCount: 2 },
              { id: 'used', name: 'Bracket', lineCount: 1 },
            ],
            totalCount: 3,
          }),
        );
      if (url.endsWith('/child') && childAttempts++ === 0)
        return Promise.resolve(
          jsonResponse({ detail: 'Used by Enclosure' }, 409),
        );
      if (url.endsWith('/used'))
        return Promise.resolve(jsonResponse({ detail: 'Used by Lid' }, 409));
      return Promise.resolve(new Response(null, { status: 204 }));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage();
    await userEvent.click(
      await screen.findByRole('checkbox', {
        name: 'Select all assemblies on this page',
      }),
    );
    await userEvent.click(
      screen.getByRole('button', { name: /^Remove selected$/ }),
    );
    await userEvent.click(
      await screen.findByRole('button', { name: 'Remove' }),
    );

    await waitFor(() =>
      expect(success).toHaveBeenCalledWith('2 assemblies removed.'),
    );
    expect(error).toHaveBeenCalledWith('Bracket: Used by Lid');
    expect(error).toHaveBeenCalledTimes(1);
  });
});
