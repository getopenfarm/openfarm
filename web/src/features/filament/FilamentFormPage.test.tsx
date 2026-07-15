import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it } from 'vitest';
import { filamentStore } from './data/mockFilamentStore';
import { FilamentFormPage } from './FilamentFormPage';

function renderAddPage() {
  return render(
    <MemoryRouter initialEntries={['/filament/new']}>
      <Routes>
        <Route path="/filament/new" element={<FilamentFormPage mode="add" />} />
        <Route path="/filament" element={<div>Filament catalog page</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

function renderEditPage(id: string) {
  return render(
    <MemoryRouter initialEntries={[`/filament/${id}/edit`]}>
      <Routes>
        <Route
          path="/filament/:id/edit"
          element={<FilamentFormPage mode="edit" />}
        />
        <Route path="/filament" element={<div>Filament catalog page</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('FilamentFormPage', () => {
  beforeEach(() => {
    filamentStore.reset();
  });

  it('adds a new filament with a variant and commits it to the store on save', async () => {
    const user = userEvent.setup();
    const before = filamentStore.getSnapshot().length;
    renderAddPage();

    await user.type(screen.getByLabelText('Brand'), 'Prusament');
    await user.type(screen.getByLabelText('Material name'), 'PLA');

    await user.click(screen.getByRole('button', { name: /add variant/i }));
    await user.type(screen.getByLabelText('Name'), 'Galaxy Black');
    await user.click(screen.getByRole('button', { name: '250 g' }));
    await user.click(screen.getByRole('button', { name: 'Add variant' }));

    await user.click(screen.getByRole('button', { name: 'Add filament' }));

    const after = filamentStore.getSnapshot();
    expect(after.length).toBe(before + 1);
    const added = after.find((entry) => entry.brand === 'Prusament');
    expect(added).toBeDefined();
    expect(added?.variants).toHaveLength(1);
    expect(added?.variants[0]?.name).toBe('Galaxy Black');
    expect(added?.variants[0]?.weightsGrams).toEqual([250]);
  });

  it('pre-fills existing data in edit mode', () => {
    const existing = filamentStore.getSnapshot()[0];
    renderEditPage(existing.id);

    expect(screen.getByDisplayValue(existing.brand)).toBeInTheDocument();
    expect(screen.getByDisplayValue(existing.material)).toBeInTheDocument();
    expect(
      screen.getByText(`Variants (${existing.variants.length})`),
    ).toBeInTheDocument();
  });
});
