import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PrintersPage } from './PrintersPage';

describe('PrintersPage', () => {
  it('renders the coming-soon heading', () => {
    render(<PrintersPage />);
    expect(
      screen.getByRole('heading', { name: 'Printers' }),
    ).toBeInTheDocument();
  });
});
