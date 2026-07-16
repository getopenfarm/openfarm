import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { spoolsStore } from './data/mockSpoolsStore';
import { SpoolsPage } from './SpoolsPage';

describe('SpoolsPage', () => {
  beforeEach(() => {
    spoolsStore.reset();
  });

  it('renders the page heading and subtitle counts', () => {
    render(<SpoolsPage />);

    expect(screen.getByRole('heading', { name: 'Spools' })).toBeInTheDocument();
    expect(screen.getByText(/Physical inventory/)).toBeInTheDocument();
    expect(screen.getByText(/12 spools/)).toBeInTheDocument();
  });

  it('renders spool rows with vendor names and colors', () => {
    render(<SpoolsPage />);

    expect(screen.getAllByText('Bambu').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Polymaker').length).toBeGreaterThan(0);
    expect(screen.getAllByText('eSUN').length).toBeGreaterThan(0);
    expect(screen.getAllByText('PLA Basic').length).toBeGreaterThan(0);
  });

  it('renders state badges for using, storage, and finished spools', () => {
    render(<SpoolsPage />);

    expect(screen.getAllByText('In use').length).toBeGreaterThan(0);
    expect(screen.getAllByText('In storage').length).toBeGreaterThan(0);
    expect(screen.getByText('Finished')).toBeInTheDocument();
  });

  it('shows an em-dash for spools without a location', () => {
    render(<SpoolsPage />);

    expect(screen.getAllByText('—').length).toBeGreaterThan(0);
  });
});
