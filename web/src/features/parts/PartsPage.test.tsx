import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router';
import { beforeEach, describe, expect, it } from 'vitest';
import { PreferencesProvider } from '@/lib/preferences/usePreferences';
import { foldersStore, partsStore } from './data/mockPartsStore';
import { PartsPage } from './PartsPage';

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <PreferencesProvider>
          <MemoryRouter>{children}</MemoryRouter>
        </PreferencesProvider>
      </QueryClientProvider>
    );
  }

  return render(<PartsPage />, { wrapper: Wrapper });
}

describe('PartsPage', () => {
  beforeEach(() => {
    partsStore.reset();
    foldersStore.reset();
    // ViewToggle persists to localStorage via usePreferences — clear so each
    // test starts from the default (table) view regardless of test order.
    window.localStorage.clear();
  });

  it('renders the library in table view by default, with loose parts only at root', () => {
    renderPage();

    expect(screen.getByText('Part library')).toBeInTheDocument();
    expect(
      screen.getByText('3 folders · 5 loose parts · 8 total'),
    ).toBeInTheDocument();

    const table = within(screen.getByRole('table'));
    expect(table.getByText('Phone stand')).toBeInTheDocument();
    // Parts inside folders are scoped out at root.
    expect(table.queryByText('Gridfinity 2×2 bin')).not.toBeInTheDocument();
  });

  it('shows folder tiles and loose parts in grid view', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('radio', { name: 'Grid view' }));

    expect(screen.getByText('Gridfinity system')).toBeInTheDocument();
    expect(screen.getByText('Keyboards')).toBeInTheDocument();
    expect(screen.getByText('Signage')).toBeInTheDocument();
    expect(screen.getByText('Phone stand')).toBeInTheDocument();
    expect(screen.queryByText('Gridfinity 2×2 bin')).not.toBeInTheDocument();
  });

  it('navigates into a folder and back out', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('radio', { name: 'Grid view' }));
    await user.click(screen.getByText('Gridfinity system'));

    expect(
      screen.getByRole('heading', { name: 'Gridfinity system' }),
    ).toBeInTheDocument();
    expect(screen.getByText('Gridfinity 2×2 bin')).toBeInTheDocument();
    expect(screen.queryByText('Phone stand')).not.toBeInTheDocument();

    await user.click(screen.getByText('Part library'));

    expect(
      screen.getByRole('heading', { name: 'Part library' }),
    ).toBeInTheDocument();
    expect(screen.getByText('Phone stand')).toBeInTheDocument();
  });

  it('filters parts by search across name and tags', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByPlaceholderText('Search parts…'), 'cable');

    const table = within(screen.getByRole('table'));
    expect(table.getByText('Cable clip')).toBeInTheDocument();
    expect(table.queryByText('Phone stand')).not.toBeInTheDocument();
  });

  it('creates a new folder that persists in the folder list', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: 'New folder' }));
    await user.type(screen.getByLabelText('Folder name'), 'Enclosures');
    await user.click(screen.getByRole('button', { name: 'Create folder' }));

    expect(
      screen.getByText('4 folders · 5 loose parts · 8 total'),
    ).toBeInTheDocument();

    await user.click(screen.getByRole('radio', { name: 'Grid view' }));
    expect(screen.getByText('Enclosures')).toBeInTheDocument();
  });
});
