import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import { ProfilesPage } from './ProfilesPage';

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
  }

  return render(<ProfilesPage />, { wrapper: Wrapper });
}

describe('ProfilesPage', () => {
  it('renders the Machine/Filament tabs and the Import trigger', () => {
    renderPage();

    expect(screen.getByText('Profiles')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Machine' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Filament' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Import' })).toBeInTheDocument();
  });

  it('shows an empty-state message before any profiles are imported', () => {
    renderPage();

    expect(
      screen.getByText(
        'No machine profiles yet. Import a slicer bundle to populate this catalog.',
      ),
    ).toBeInTheDocument();
  });
});
