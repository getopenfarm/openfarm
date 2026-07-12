import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { JobsPage } from './JobsPage';

describe('JobsPage', () => {
  it('renders the coming-soon heading', () => {
    render(<JobsPage />);
    expect(screen.getByRole('heading', { name: 'Jobs' })).toBeInTheDocument();
  });
});
