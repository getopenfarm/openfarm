import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { partsStore } from './data/mockPartsStore';
import { PartAddPage } from './PartAddPage';
import { PartEditPage } from './PartEditPage';

function renderApp() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/parts/new']}>
        <Routes>
          <Route path="/parts/new" element={<PartAddPage />} />
          <Route path="/parts/:id/edit" element={<PartEditPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('PartAddPage', () => {
  beforeEach(() => {
    partsStore.reset();
    vi.useFakeTimers();
    // The edit page mounts the shared Print modal (closed), which fetches
    // the fleet list via react-query regardless of dialog visibility.
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({ printers: [] }))),
    );
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('shows a parsing state then navigates to the edit page with the parsed result', async () => {
    renderApp();

    fireEvent.click(screen.getByRole('button', { name: 'Upload part file' }));

    expect(screen.getByText('Parsing your file…')).toBeInTheDocument();

    await act(async () => {
      vi.advanceTimersByTime(1000);
    });

    // Landed on the edit page, pre-filled with the simulated parsed result.
    expect(screen.getByDisplayValue('Bracket Mount V1')).toBeInTheDocument();
    expect(screen.getByText('1h 12m')).toBeInTheDocument();
    expect(screen.getByText('38 g')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Create part' }),
    ).toBeInTheDocument();

    // Nothing was committed to the store yet — only "Create part" does that.
    expect(
      partsStore.getSnapshot().some((part) => part.name === 'Bracket Mount V1'),
    ).toBe(false);
  });

  it('does not start a second parse while one is already in flight', async () => {
    renderApp();

    const dropzone = screen.getByRole('button', { name: 'Upload part file' });
    fireEvent.click(dropzone);
    fireEvent.click(dropzone);

    await act(async () => {
      vi.advanceTimersByTime(1000);
    });

    // Only one part reaches the edit page's draft state (no duplicate navigation).
    expect(screen.getByDisplayValue('Bracket Mount V1')).toBeInTheDocument();
  });
});
