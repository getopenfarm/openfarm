import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PartsPage } from './PartsPage';

vi.mock('./hooks/usePartsHub', () => ({ usePartsHub: () => {} }));

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function setup({
  fail = false,
  count = 26,
}: {
  fail?: boolean;
  count?: number;
} = {}) {
  let total = count;
  let failing = fail;
  let sortGate: Promise<void> | undefined;
  let releaseSort: (() => void) | undefined;
  const requests = vi.fn(
    async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(input.toString(), 'http://localhost');
      if (url.pathname === '/api/v1/tags')
        return json({ tags: [{ id: 'tag-1', name: 'Brackets' }] });
      if (input.toString().endsWith('/deletion-check')) {
        return Promise.resolve(
          new Response(
            JSON.stringify({
              parts: JSON.parse(init?.body as string).partIds.map(
                (partId: string) => ({
                  partId,
                  status: 'Eligible',
                  blockers: [],
                  legacyJobsLosingRecovery: 0,
                }),
              ),
            }),
            { headers: { 'Content-Type': 'application/json' } },
          ),
        );
      }
      if (init?.method === 'DELETE') {
        total--;
        return new Response(null, { status: 204 });
      }
      if (url.searchParams.has('sortBy')) await sortGate;
      if (failing) return json({ detail: 'Offline' }, 503);
      const page = Number(url.searchParams.get('page'));
      const pageSize = Number(url.searchParams.get('pageSize'));
      const filtered = url.searchParams.has('tag');
      const length = filtered
        ? 0
        : Math.max(0, Math.min(pageSize, total - (page - 1) * pageSize));
      return json({
        totalCount: filtered ? 0 : total,
        parts: Array.from({ length }, (_, i) => ({
          id: `part-${(page - 1) * pageSize + i + 1}`,
          name: `Part ${(page - 1) * pageSize + i + 1}`,
          fileFormat: 'STL',
          printRequirementsStatus: 'Complete',
          tags: [],
        })),
      });
    },
  );
  vi.stubGlobal('fetch', requests);
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <PartsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return {
    requests,
    failRequests: () => {
      failing = true;
    },
    holdSorting: () => {
      sortGate = new Promise<void>((resolve) => {
        releaseSort = resolve;
      });
      return () => {
        sortGate = undefined;
        releaseSort?.();
      };
    },
    recover: () => {
      failing = false;
    },
  };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('PartsPage', () => {
  it('changes the server page size and clears page-local selection', async () => {
    const { requests } = setup();
    const user = userEvent.setup();
    await screen.findByText('1–25 of 26');
    await user.click(screen.getByRole('checkbox', { name: 'Select Part 1' }));
    expect(
      screen.getByRole('region', { name: 'Parts actions' }),
    ).toBeInTheDocument();
    await user.click(screen.getByRole('combobox', { name: 'Rows per page' }));
    await user.click(screen.getByRole('option', { name: '10' }));
    expect(await screen.findByText('1–10 of 26')).toBeInTheDocument();
    expect(
      screen.queryByRole('region', { name: 'Parts actions' }),
    ).not.toBeInTheDocument();
    const url = new URL(
      requests.mock.calls.at(-1)?.[0].toString() ?? '',
      'http://localhost',
    );
    expect(url.searchParams.get('pageSize')).toBe('10');
    expect(url.searchParams.get('page')).toBe('1');
  });

  it('fetches valid server pages', async () => {
    setup();
    const user = userEvent.setup();
    expect(await screen.findByText('1–25 of 26')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Previous parts page' }),
    ).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Next parts page' }));
    expect(
      await screen.findByRole('link', { name: 'Part 26' }),
    ).toBeInTheDocument();
    expect(screen.getByText('26–26 of 26')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Next parts page' }),
    ).toBeDisabled();
  });

  it('returns to the previous page after removing its final part', async () => {
    setup();
    const user = userEvent.setup();
    await screen.findByText('1–25 of 26');
    await user.click(screen.getByRole('button', { name: 'Next parts page' }));
    await screen.findByRole('link', { name: 'Part 26' });
    await user.click(
      screen.getByRole('button', { name: 'Actions for Part 26' }),
    );
    await user.click(screen.getByRole('menuitem', { name: 'Remove' }));
    await user.click(
      await within(await screen.findByRole('alertdialog')).findByRole(
        'button',
        {
          name: 'Remove part',
        },
      ),
    );
    expect(await screen.findByText('1–25 of 25')).toBeInTheDocument();
    await waitFor(() =>
      expect(
        screen.queryByRole('link', { name: 'Part 26' }),
      ).not.toBeInTheDocument(),
    );
    expect(screen.getByRole('button', { name: 'Page 1' })).toBeInTheDocument();
  });

  it('clears the floating bar when switching pages and recovers after bulk-removing the last page', async () => {
    setup();
    await screen.findByText('1–25 of 26');
    await userEvent.click(
      screen.getByRole('checkbox', { name: 'Select Part 1' }),
    );
    expect(
      screen.getByRole('region', { name: 'Parts actions' }),
    ).toHaveTextContent('1 selected');
    await userEvent.click(
      screen.getByRole('button', { name: 'Next parts page' }),
    );
    await screen.findByRole('link', { name: 'Part 26' });
    expect(
      screen.queryByRole('region', { name: 'Parts actions' }),
    ).not.toBeInTheDocument();
    await userEvent.click(
      screen.getByRole('checkbox', { name: 'Select all parts on this page' }),
    );
    await userEvent.click(
      screen.getByRole('button', { name: 'Remove selected' }),
    );
    await userEvent.click(
      await within(await screen.findByRole('alertdialog')).findByRole(
        'button',
        {
          name: 'Remove part',
        },
      ),
    );
    expect(await screen.findByText('1–25 of 25')).toBeInTheDocument();
    expect(
      screen.queryByRole('region', { name: 'Parts actions' }),
    ).not.toBeInTheDocument();
  });

  it('shows a load error with Retry instead of an empty library', async () => {
    const { recover } = setup({ fail: true });
    expect(await screen.findByText('Could not load parts')).toBeInTheDocument();
    expect(screen.queryByText('No parts')).not.toBeInTheDocument();
    recover();
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(
      await screen.findByRole('link', { name: 'Part 1' }),
    ).toBeInTheDocument();
  });

  it('shows the genuine empty library state', async () => {
    setup({ count: 0 });
    expect(await screen.findByText('No parts')).toBeInTheDocument();
    expect(
      screen.queryByRole('navigation', { name: 'Parts pagination' }),
    ).not.toBeInTheDocument();
  });
});

it('clears page-local selection on sort and preserves header focus while loading', async () => {
  const { requests, holdSorting } = setup();
  const user = userEvent.setup();
  await screen.findByText('1–25 of 26');
  await user.click(screen.getByRole('checkbox', { name: 'Select Part 1' }));
  const release = holdSorting();
  const header = screen.getByRole('button', { name: 'Part' });
  header.focus();
  await user.keyboard('{Enter}');
  await waitFor(() => expect(header).toBeDisabled());
  expect(
    screen.queryByRole('region', { name: 'Parts actions' }),
  ).not.toBeInTheDocument();
  expect(
    screen.getByRole('checkbox', { name: 'Select Part 1' }),
  ).toBeDisabled();
  expect(screen.getByText('Part 1')).toBeInTheDocument();
  expect(header).toHaveFocus();
  const request = requests.mock.calls
    .map(([url]) => new URL(url.toString()))
    .findLast((url) => url.pathname === '/api/v1/parts');
  expect(request?.searchParams.get('sortBy')).toBe('name');
  expect(request?.searchParams.get('sortDirection')).toBe('asc');
  expect(request?.searchParams.get('page')).toBe('1');
  release();
  await waitFor(() => expect(header).toBeEnabled());
  expect(header).toHaveFocus();
  expect(header.closest('th')).toHaveAttribute('aria-sort', 'ascending');
});

it('keeps previous rows after a failed sort and retries the same requested order', async () => {
  const { requests, failRequests, recover } = setup();
  const user = userEvent.setup();
  await screen.findByText('Part 1');
  failRequests();
  const header = screen.getByRole('button', { name: 'Part' });
  await user.click(header);
  await screen.findByText('Could not refresh list');
  expect(screen.getByText('Part 1')).toBeInTheDocument();
  expect(header).toBeDisabled();
  expect(
    screen.getByRole('checkbox', { name: 'Select Part 1' }),
  ).toBeDisabled();
  recover();
  await user.click(screen.getByRole('button', { name: 'Retry' }));
  await waitFor(() => expect(header).toBeEnabled());
  const request = requests.mock.calls
    .map(([url]) => new URL(url.toString()))
    .findLast((url) => url.pathname === '/api/v1/parts');
  expect(request?.searchParams.get('sortDirection')).toBe('asc');
  expect(header.closest('th')).toHaveAttribute('aria-sort', 'ascending');
});
