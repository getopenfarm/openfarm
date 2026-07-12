import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AssembliesPage } from './AssembliesPage';

describe('AssembliesPage', () => {
  it('renders the coming-soon heading', () => {
    render(<AssembliesPage />);
    expect(
      screen.getByRole('heading', { name: 'Assemblies' }),
    ).toBeInTheDocument();
  });
});
