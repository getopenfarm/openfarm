import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router';
import { describe, expect, it } from 'vitest';
import { FilamentCatalogTable } from './FilamentCatalogTable';

describe('FilamentCatalogTable actions', () => {
  it('opens the existing variant editor from the row menu', async () => {
    render(
      <MemoryRouter>
        <Routes>
          <Route
            path="/"
            element={
              <FilamentCatalogTable
                filaments={[
                  { id: 'f1', vendorName: 'Vendor', material: 'PLA' },
                ]}
                stock={[]}
              />
            }
          />
          <Route path="/filament/:id/edit" element={<h1>Variant editor</h1>} />
        </Routes>
      </MemoryRouter>,
    );
    expect(
      screen.queryByRole('button', { name: 'Add variant' }),
    ).not.toBeInTheDocument();
    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Vendor PLA' }),
    );
    await userEvent.click(
      screen.getByRole('menuitem', { name: 'Add variant' }),
    );
    expect(
      screen.getByRole('heading', { name: 'Variant editor' }),
    ).toBeInTheDocument();
  });
  it("adds up each filament's colors into on hand, value and used", () => {
    render(
      <MemoryRouter>
        <FilamentCatalogTable
          filaments={[
            {
              id: 'f1',
              vendorName: 'Vendor',
              material: 'PLA',
              variants: [{ id: 'black' }, { id: 'red' }],
            },
          ]}
          stock={[
            {
              filamentVariantId: 'black',
              onHandGrams: 900,
              activeSpoolCount: 1,
              stockValue: 18,
              unpricedSpoolCount: 0,
              usedLast30DaysGrams: 100,
              isLow: false,
            },
            {
              filamentVariantId: 'red',
              onHandGrams: 300,
              activeSpoolCount: 1,
              stockValue: 0,
              unpricedSpoolCount: 1,
              usedLast30DaysGrams: 0,
              isLow: true,
            },
          ]}
        />
      </MemoryRouter>,
    );

    const row = screen.getAllByRole('row')[1];
    expect(row).toHaveTextContent('1.2 kg');
    expect(row).toHaveTextContent('1 low');
    expect(row).toHaveTextContent('18.00');
    expect(row).toHaveTextContent('Partial');
    expect(row).toHaveTextContent('100 g');
  });
});
