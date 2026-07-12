import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { FilamentPage } from './FilamentPage';

describe('FilamentPage', () => {
  it('renders the coming-soon heading', () => {
    render(<FilamentPage />);
    expect(
      screen.getByRole('heading', { name: 'Filament' }),
    ).toBeInTheDocument();
  });
});
