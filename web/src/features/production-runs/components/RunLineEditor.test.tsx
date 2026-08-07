import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { type EditableRunLine, RunLineEditor } from './RunLineEditor';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function stubFetch() {
  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/api/v1/parts')) {
      return Promise.resolve(
        jsonResponse({ parts: [{ id: 'part-1', name: 'Bracket' }] }),
      );
    }
    if (url.includes('/api/v1/assemblies')) {
      return Promise.resolve(jsonResponse({ items: [] }));
    }
    if (url.includes('/api/v1/printers')) {
      return Promise.resolve(jsonResponse({ printers: [] }));
    }
    return Promise.reject(new Error(`Unexpected fetch: ${url}`));
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

function Harness() {
  const [lines, setLines] = useState<EditableRunLine[]>([]);
  return <RunLineEditor lines={lines} onChange={setLines} />;
}

function renderEditor() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }

  return render(<Harness />, { wrapper: Wrapper });
}

describe('RunLineEditor revision-mode toggle', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('defaults a new line to Snapshot revision mode', async () => {
    stubFetch();
    const user = userEvent.setup();
    renderEditor();

    await user.click(await screen.findByRole('combobox', { name: 'Target' }));
    await user.click(await screen.findByText('Bracket'));
    await user.click(screen.getByRole('button', { name: /add line/i }));

    const row = (await screen.findByText('Bracket')).closest(
      '.rounded-md.border',
    );
    expect(row).not.toBeNull();
    expect(
      within(row as HTMLElement).getByText('Snapshot'),
    ).toBeInTheDocument();
  });

  it('records Follow-latest when the toggle is switched before adding', async () => {
    stubFetch();
    const user = userEvent.setup();
    renderEditor();

    await user.click(await screen.findByRole('combobox', { name: 'Target' }));
    await user.click(await screen.findByText('Bracket'));

    await user.click(screen.getByRole('combobox', { name: 'Revision mode' }));
    await user.click(await screen.findByText('Follow-latest'));

    await user.click(screen.getByRole('button', { name: /add line/i }));

    const row = (await screen.findByText('Bracket')).closest(
      '.rounded-md.border',
    );
    expect(row).not.toBeNull();
    expect(
      within(row as HTMLElement).getByText('Follow-latest'),
    ).toBeInTheDocument();
  });
});
