import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { beforeEach, describe, expect, it } from 'vitest';
import { filamentStore } from './data/mockFilamentStore';
import { FilamentPage } from './FilamentPage';

function renderPage() {
  return render(
    <MemoryRouter>
      <FilamentPage />
    </MemoryRouter>,
  );
}

describe('FilamentPage', () => {
  beforeEach(() => {
    filamentStore.reset();
  });

  it('renders the catalog heading and subtitle counts', () => {
    const total = filamentStore.getSnapshot().length;
    renderPage();

    expect(
      screen.getByRole('heading', { name: 'Filament' }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(new RegExp(`Catalog · ${total} materials`)),
    ).toBeInTheDocument();
  });

  it('renders brand/material text and material type badges', () => {
    renderPage();

    expect(screen.getAllByText('Bambu Lab · PLA Basic').length).toBeGreaterThan(
      0,
    );
    expect(screen.getByText('Polymaker · PolyTerra PLA')).toBeInTheDocument();
    expect(screen.getAllByText('PLA').length).toBeGreaterThan(0);
    expect(screen.getByText('PETG')).toBeInTheDocument();
    expect(screen.getByText('ASA')).toBeInTheDocument();
  });

  it('has an Add filament button', () => {
    renderPage();
    expect(
      screen.getByRole('button', { name: 'Add filament' }),
    ).toBeInTheDocument();
  });
});
