import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AssembliesPage } from '@/features/assemblies/AssembliesPage';
import { NonPrintedComponentsPage } from '@/features/non-printed-components/NonPrintedComponentsPage';
import { ProductionRunsPage } from '@/features/production-runs/ProductionRunsPage';
import { ProfilesPage } from '@/features/profiles/ProfilesPage';

vi.mock('@/features/production-runs/hooks/useProductionHub', () => ({
  useProductionHub: () => {},
}));
afterEach(() => vi.unstubAllGlobals());

function setup(Page: typeof AssembliesPage, failSecondPage = false) {
  let fail = failSecondPage;
  const requests: URL[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(input.toString(), 'http://localhost');
      requests.push(url);
      const page = Number(url.searchParams.get('page'));
      return new Response(
        JSON.stringify(
          fail && page === 2
            ? { detail: 'Offline' }
            : {
                items: [
                  {
                    id: `item-${page}`,
                    name: `Record ${page}`,
                    displayLabel: `Record ${page}`,
                  },
                ],
                totalCount: 26,
              },
        ),
        {
          status: fail && page === 2 ? 503 : 200,
          headers: { 'Content-Type': 'application/json' },
        },
      );
    }),
  );
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <Page />
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return {
    requests,
    recover: () => {
      fail = false;
    },
  };
}

describe('Server-paginated resource lists', () => {
  it.each([
    ['assemblies', AssembliesPage],
    ['non-printed-components', NonPrintedComponentsPage],
    ['production-runs', ProductionRunsPage],
  ] as const)(
    'fetches later %s pages and retries failures on the requested page',
    async (path, Page) => {
      const { requests, recover } = setup(Page, true);
      const user = userEvent.setup();
      expect(await screen.findByText('Record 1')).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Next table page' }));
      await user.click(await screen.findByRole('button', { name: 'Retry' }));
      expect(requests.at(-1)?.searchParams.get('page')).toBe('2');
      recover();
      await user.click(await screen.findByRole('button', { name: 'Retry' }));
      expect(await screen.findByText('Record 2')).toBeInTheDocument();
      expect(screen.getByText('26–26 of 26')).toBeInTheDocument();
      expect(
        requests.every(
          (url) =>
            url.pathname === `/api/v1/${path}` &&
            url.searchParams.get('pageSize') === '25',
        ),
      ).toBe(true);
      await user.click(
        screen.getByRole('button', { name: 'Previous table page' }),
      );
      expect(await screen.findByText('Record 1')).toBeInTheDocument();
      await user.click(screen.getByRole('combobox', { name: 'Rows per page' }));
      await user.click(screen.getByRole('option', { name: '10' }));
      expect(await screen.findByText('1–10 of 26')).toBeInTheDocument();
      expect(requests.at(-1)?.searchParams.get('pageSize')).toBe('10');
      expect(requests.at(-1)?.searchParams.get('page')).toBe('1');
    },
  );

  it('keeps machine and filament page positions independent when switching profile tabs', async () => {
    const { requests } = setup(ProfilesPage);
    const user = userEvent.setup();
    await screen.findByText('Record 1');
    await user.click(screen.getByRole('button', { name: 'Next table page' }));
    expect(await screen.findByText('Record 2')).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: 'Filament' }));
    expect(await screen.findByText('Record 1')).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: 'Machine' }));
    expect(await screen.findByText('Record 2')).toBeInTheDocument();
    expect(
      requests.some(
        (url) =>
          url.pathname.endsWith('/profiles/machine') &&
          url.searchParams.get('page') === '2',
      ),
    ).toBe(true);
    expect(
      requests
        .filter((url) => url.pathname.endsWith('/profiles/filament'))
        .every((url) => url.searchParams.get('page') === '1'),
    ).toBe(true);
  });
});

it.each([
  [AssembliesPage, 'Assembly', 'name'],
  [NonPrintedComponentsPage, 'Name', 'name'],
  [ProductionRunsPage, 'Run', 'displayLabel'],
] as const)(
  'sorts a resource globally and resets its page (%s)',
  async (Page, title, field) => {
    const { requests } = setup(Page);
    const user = userEvent.setup();
    await screen.findByText('Record 1');
    expect(requests[0].searchParams.has('sortBy')).toBe(false);
    await user.click(screen.getByRole('button', { name: 'Next table page' }));
    await screen.findByText('Record 2');
    const header = screen.getByRole('button', { name: title });
    header.focus();
    await user.keyboard('{Enter}');
    await screen.findByText('Record 1');
    expect(requests.at(-1)?.searchParams.get('page')).toBe('1');
    expect(requests.at(-1)?.searchParams.get('sortBy')).toBe(field);
    expect(requests.at(-1)?.searchParams.get('sortDirection')).toBe('asc');
    expect(header.closest('th')).toHaveAttribute('aria-sort', 'ascending');
    expect(header).toHaveFocus();
    await user.keyboard(' ');
    await screen.findByText('Record 1');
    expect(requests.at(-1)?.searchParams.get('sortDirection')).toBe('desc');
  },
);

it('preserves independent profile sorting and resets only the sorted tab page', async () => {
  const { requests } = setup(ProfilesPage);
  const user = userEvent.setup();
  await screen.findByText('Record 1');
  await user.click(screen.getByRole('button', { name: 'Next table page' }));
  await screen.findByText('Record 2');
  await user.click(screen.getByRole('button', { name: 'Name' }));
  await screen.findByText('Record 1');
  expect(
    screen.getByRole('button', { name: 'Name' }).closest('th'),
  ).toHaveAttribute('aria-sort', 'descending');
  await user.click(screen.getByRole('tab', { name: 'Filament' }));
  expect(
    screen.getByRole('button', { name: 'Name' }).closest('th'),
  ).toHaveAttribute('aria-sort', 'ascending');
  await user.click(screen.getByRole('tab', { name: 'Machine' }));
  expect(
    screen.getByRole('button', { name: 'Name' }).closest('th'),
  ).toHaveAttribute('aria-sort', 'descending');
  expect(
    requests
      .filter((url) => url.pathname.endsWith('/profiles/filament'))
      .every(
        (url) =>
          url.searchParams.get('sortDirection') === 'asc' &&
          url.searchParams.get('page') === '1',
      ),
  ).toBe(true);
});
