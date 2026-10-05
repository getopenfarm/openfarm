import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PartEstimate } from './PartEstimate';

describe('PartEstimate', () => {
  it('shows unknown fields without fabricated zero values or a spinner', () => {
    render(<PartEstimate status="Unavailable" minutes={null} grams={null} />);
    expect(
      screen.getByText('Time unavailable · Weight unavailable'),
    ).toBeInTheDocument();
    expect(screen.queryByText('Estimating…')).not.toBeInTheDocument();
  });
  it('retains a known weight independently from time', () => {
    render(<PartEstimate status="Partial" minutes={null} grams={8.5} />);
    expect(screen.getByText('Time unavailable · 8.5 g')).toBeInTheDocument();
  });
  it('shows the spinner only for active estimation', () => {
    render(<PartEstimate status="Resolving" />);
    expect(screen.getByText('Estimating…')).toBeInTheDocument();
  });
});
