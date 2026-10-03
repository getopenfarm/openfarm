import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { RevisionModeControl } from './RevisionModeControl';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFetch() {
  const fetchMock = vi.fn(() =>
    Promise.resolve(
      jsonResponse({ id: 'line-1', revisionMode: 'FollowLatest' }),
    ),
  );
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function renderControl(revisionMode = 'Snapshot') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }

  return render(
    <RevisionModeControl
      runId="run-1"
      lineId="line-1"
      revisionMode={revisionMode}
    />,
    { wrapper: Wrapper },
  );
}

describe('RevisionModeControl', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows the current revision mode', () => {
    stubFetch();
    renderControl('Snapshot');

    expect(screen.getByText('Snapshot')).toBeInTheDocument();
  });

  it('submits a change to Follow-latest', async () => {
    const fetchMock = stubFetch();
    const user = userEvent.setup();
    renderControl('Snapshot');

    await user.click(screen.getByRole('combobox'));
    await user.click(await screen.findByText('Follow-latest'));

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining(
        '/api/v1/production-runs/run-1/lines/line-1/revision-mode',
      ),
      expect.objectContaining({ method: 'PUT' }),
    );
  });
});
