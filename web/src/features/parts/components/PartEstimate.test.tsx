import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { filamentGrams, PartEstimate } from './PartEstimate';

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
  it('says when the weight includes purge', () => {
    render(<PartEstimate size="lg" minutes={60} grams={12} includesPurge />);
    expect(screen.getByText('Filament · incl. purge')).toBeInTheDocument();
  });
});

describe('filamentGrams', () => {
  it('sums the colours when every colour has grams', () => {
    expect(
      filamentGrams([{ estimatedGrams: 7.5 }, { estimatedGrams: 2.5 }], 9),
    ).toBe(10);
  });
  it('falls back to the slicer total when any colour lacks grams', () => {
    expect(filamentGrams([{ estimatedGrams: 7.5 }, {}], 9)).toBe(9);
    expect(filamentGrams([], 9)).toBe(9);
  });
});
