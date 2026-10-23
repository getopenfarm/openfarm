import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { describe, expect, it } from 'vitest';
import { FilamentCatalogTable } from './FilamentCatalogTable';

function Search() {
  return <h1>{useLocation().search}</h1>;
}

describe('FilamentCatalogTable actions', () => {
  function renderRows() {
    render(
      <MemoryRouter>
        <Routes>
          <Route
            path="/"
            element={
              <FilamentCatalogTable
                filaments={[
                  {
                    id: 'f1',
                    vendorName: 'Vendor',
                    material: 'PLA',
                    variants: [{ id: 'v1' }],
                  },
                ]}
                stock={[]}
              />
            }
          />
          <Route path="/filament/:id/edit" element={<h1>Filament editor</h1>} />
          <Route path="/filament/spools/new" element={<Search />} />
        </Routes>
      </MemoryRouter>,
    );
  }

  it('links each filament to its page', () => {
    renderRows();

    expect(screen.getByRole('link', { name: 'Vendor · PLA' })).toHaveAttribute(
      'href',
      '/filament/f1',
    );
  });

  it('adds a spool of its only color from the row menu', async () => {
    renderRows();

    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Vendor PLA' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Add spools' }));

    expect(screen.getByRole('heading')).toHaveTextContent(
      '?filamentId=f1&variantId=v1',
    );
  });

  it('opens the editor from the row menu', async () => {
    renderRows();

    await userEvent.click(
      screen.getByRole('button', { name: 'Actions for Vendor PLA' }),
    );
    await userEvent.click(screen.getByRole('menuitem', { name: 'Edit' }));

    expect(
      screen.getByRole('heading', { name: 'Filament editor' }),
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
  it('flags a filament whose queued work needs more than is on hand', () => {
    render(
      <MemoryRouter>
        <FilamentCatalogTable
          filaments={[
            {
              id: 'f1',
              vendorName: 'Vendor',
              material: 'PLA',
              variants: [{ id: 'teal' }],
            },
          ]}
          stock={[{ filamentVariantId: 'teal', onHandGrams: 100 }]}
          shortages={[
            { filamentVariantId: 'teal', queuedGrams: 1300, onHandGrams: 100 },
          ]}
        />
      </MemoryRouter>,
    );

    const badge = screen.getByText('Short 1.2 kg');
    expect(badge.closest('[title]')).toHaveAttribute(
      'title',
      'Queued 1.3 kg · on hand 100 g',
    );
  });
});
