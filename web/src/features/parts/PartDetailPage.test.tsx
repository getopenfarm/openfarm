import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Toaster } from '@/components/ui/sonner';
import { PartDetailPage } from './PartDetailPage';

function renderPage(partId: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/parts/${partId}`]}>
          {children}
          <Toaster />
        </MemoryRouter>
      </QueryClientProvider>
    );
  }

  return render(
    <Routes>
      <Route path="/parts/:id" element={<PartDetailPage />} />
      <Route path="/parts" element={<div>Parts list page</div>} />
    </Routes>,
    { wrapper: Wrapper },
  );
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function partBody(id: string, name: string) {
  return {
    id,
    name,
    fileFormat: 'STL',
    nozzleDiameterMm: 0.4,
    requiresSlicing: true,
    tags: [],
    thumbnails: [],
    plateCount: 1,
    estimateStatus: 'Unavailable',
    estimatedPrintTimeMinutes: null,
    estimatedFilamentWeightGrams: null,
    filamentRequirements: [],
  };
}

describe('PartDetailPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows a working Delete action that navigates back to the parts list', async () => {
    const partId = '11111111-1111-1111-1111-111111111111';
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      if (init?.method === 'DELETE') {
        return Promise.resolve(new Response(null, { status: 204 }));
      }
      return Promise.resolve(jsonResponse(partBody(partId, 'Removable Part')));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage(partId);

    expect(await screen.findByText('Removable Part')).toBeInTheDocument();
    const deleteButton = screen.getByRole('button', { name: 'Delete' });
    await user.click(deleteButton);

    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText('Delete part?')).toBeInTheDocument();
    const confirmButton = within(dialog).getByRole('button', {
      name: 'Delete',
    });
    await user.click(confirmButton);

    expect(await screen.findByText('Parts list page')).toBeInTheDocument();
    expect(
      fetchMock.mock.calls.some(
        (call) =>
          call[0].toString().includes(`/api/v1/parts/${partId}`) &&
          (call[1] as RequestInit | undefined)?.method === 'DELETE',
      ),
    ).toBe(true);
  });

  it('does not delete the part when the confirmation is declined', async () => {
    const partId = '22222222-2222-2222-2222-222222222222';
    const user = userEvent.setup();
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/hubs/farm')) {
        return Promise.reject(new Error('SignalR not available in tests'));
      }
      if (init?.method === 'DELETE') {
        throw new Error(
          'DELETE should not be called when confirmation is declined',
        );
      }
      return Promise.resolve(jsonResponse(partBody(partId, 'Kept Part')));
    });
    vi.stubGlobal('fetch', fetchMock);

    renderPage(partId);

    expect(await screen.findByText('Kept Part')).toBeInTheDocument();
    const deleteButton = screen.getByRole('button', { name: 'Delete' });
    await user.click(deleteButton);

    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText('Delete part?')).toBeInTheDocument();
    const cancelButton = within(dialog).getByRole('button', {
      name: 'Cancel',
    });
    await user.click(cancelButton);

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.getByText('Kept Part')).toBeInTheDocument();
  });
});
