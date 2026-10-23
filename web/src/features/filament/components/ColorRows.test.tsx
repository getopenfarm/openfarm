import { render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { describe, expect, it } from 'vitest';
import type {
  FilamentResponse,
  VariantStockResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { ColorRows } from './ColorRows';

const FILAMENT: FilamentResponse = {
  id: 'f-1',
  vendorName: 'Bambu',
  material: 'PLA Basic',
  variants: [
    { id: 'teal', colorName: 'Teal', colorHex: '#008080' },
    { id: 'white', colorName: 'White', colorHex: '#ffffff' },
    { id: 'black', colorName: 'Black', colorHex: '#000000' },
  ],
};

const stock = (
  filamentVariantId: string,
  onHandGrams: number,
  usedLast30DaysGrams: number,
  daysLeft: number | null,
): VariantStockResponse => ({
  filamentVariantId,
  onHandGrams,
  activeSpoolCount: 1,
  stockValue: 0,
  unpricedSpoolCount: 0,
  usedLast30DaysGrams,
  daysLeft,
  isLow: false,
});

function renderRows(rows: VariantStockResponse[]) {
  render(
    <MemoryRouter>
      <ColorRows filament={FILAMENT} stock={rows} shortages={[]} spools={[]} />
    </MemoryRouter>,
  );
  return within(screen.getByRole('list', { name: 'Colors' })).getAllByRole(
    'listitem',
  );
}

describe('ColorRows', () => {
  it('puts the most used color first, then the most stocked, and says when a color sat idle', () => {
    const rows = renderRows([
      stock('teal', 1000, 0, null),
      stock('white', 3000, 0, null),
      stock('black', 2000, 600, 100),
    ]);

    ['Black', 'White', 'Teal'].forEach((name, index) => {
      expect(within(rows[index]).getByText(name)).toBeInTheDocument();
    });
    expect(rows[1]).toHaveTextContent('No use in 30 d');
    expect(rows[2]).toHaveTextContent('No use in 30 d');
  });

  it('caps days left at the 60-day horizon', () => {
    const rows = renderRows([stock('black', 2000, 600, 100)]);

    expect(rows[0]).toHaveTextContent('60+ d');
  });
});
