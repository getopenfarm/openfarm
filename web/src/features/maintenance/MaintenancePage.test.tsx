import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MaintenancePage } from './MaintenancePage';

describe('MaintenancePage', () => {
  it('renders the coming-soon heading', () => {
    render(<MaintenancePage />);
    expect(
      screen.getByRole('heading', { name: 'Maintenance' }),
    ).toBeInTheDocument();
  });
});
