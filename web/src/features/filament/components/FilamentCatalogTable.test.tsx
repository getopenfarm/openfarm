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
});
