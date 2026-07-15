import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { jobsStore } from './data/mockJobsStore';
import { JobsPage } from './JobsPage';

describe('JobsPage', () => {
  beforeEach(() => {
    jobsStore.reset();
  });

  it('renders the print queue with job names and status badges', () => {
    render(<JobsPage />);

    expect(screen.getByText('Print queue')).toBeInTheDocument();
    expect(screen.getByText('Gridfinity bins ×24')).toBeInTheDocument();
    expect(screen.getByText('Prototype cover')).toBeInTheDocument();
    expect(screen.getByText('Phone stand v3 ×6')).toBeInTheDocument();

    expect(screen.getAllByText('Printing').length).toBe(7);
    expect(screen.getAllByText('Queued').length).toBe(3);
    expect(screen.getByText('Paused')).toBeInTheDocument();
    expect(screen.getByText('Completed')).toBeInTheDocument();
    expect(screen.getByText('Failed')).toBeInTheDocument();
  });

  it('shows the computed summary counts in the header', () => {
    render(<JobsPage />);

    expect(
      screen.getByText('13 jobs · 7 printing · 3 queued'),
    ).toBeInTheDocument();
  });

  it('renders an em-dash for unassigned printers', () => {
    render(<JobsPage />);

    const dashes = screen.getAllByText('—');
    expect(dashes.length).toBeGreaterThan(0);
  });
});
