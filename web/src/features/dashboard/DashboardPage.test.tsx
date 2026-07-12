import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { DashboardPage } from './DashboardPage';

describe('DashboardPage', () => {
  it('renders the heading', () => {
    render(<DashboardPage />);
    expect(
      screen.getByRole('heading', { name: 'Farm Overview' }),
    ).toBeInTheDocument();
  });

  it('renders the sample printer fleet', () => {
    render(<DashboardPage />);
    expect(screen.getByText('Printer 01')).toBeInTheDocument();
    expect(screen.getByText('Printer 06')).toBeInTheDocument();
  });
});
