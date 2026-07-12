import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PartsPage } from './PartsPage';

describe('PartsPage', () => {
  it('renders the coming-soon heading', () => {
    render(<PartsPage />);
    expect(screen.getByRole('heading', { name: 'Parts' })).toBeInTheDocument();
  });
});
