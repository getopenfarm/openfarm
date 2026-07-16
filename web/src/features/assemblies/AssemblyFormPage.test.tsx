import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it } from 'vitest';
import { partsStore } from '@/features/parts/data/mockPartsStore';
import { AssemblyFormPage } from './AssemblyFormPage';
import { assembliesStore } from './data/mockAssembliesStore';

function renderApp(initialEntries: string[]) {
  return render(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route
          path="/assemblies/new"
          element={<AssemblyFormPage mode="add" />}
        />
        <Route
          path="/assemblies/:id/edit"
          element={<AssemblyFormPage mode="edit" />}
        />
        <Route path="/assemblies" element={<div>Assemblies list page</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('AssemblyFormPage', () => {
  beforeEach(() => {
    assembliesStore.reset();
    partsStore.reset();
  });

  it('adds a new assembly with a part component', async () => {
    const user = userEvent.setup();
    const before = assembliesStore.getSnapshot().length;
    renderApp(['/assemblies/new']);

    await user.type(screen.getByLabelText('Assembly name'), 'Test assembly');

    // Open the picker (header button) and confirm the preselected part
    // (the dialog's own footer button, scoped so it doesn't collide with
    // the still-present header "Add part" button behind the modal).
    await user.click(screen.getByRole('button', { name: 'Add part' }));
    const dialog = screen.getByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Add part' }));

    expect(screen.getByText('Parts · 1')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Create assembly' }));

    expect(screen.getByText('Assemblies list page')).toBeInTheDocument();
    expect(assembliesStore.getSnapshot()).toHaveLength(before + 1);

    const created = assembliesStore
      .getSnapshot()
      .find((a) => a.name === 'Test assembly');
    expect(created).toBeDefined();
    expect(created?.components).toHaveLength(1);
  });

  it('does not commit anything to the store until Create is clicked', async () => {
    const user = userEvent.setup();
    const before = assembliesStore.getSnapshot().length;
    renderApp(['/assemblies/new']);

    await user.type(screen.getByLabelText('Assembly name'), 'Untitled draft');

    expect(assembliesStore.getSnapshot()).toHaveLength(before);

    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(screen.getByText('Assemblies list page')).toBeInTheDocument();
    expect(assembliesStore.getSnapshot()).toHaveLength(before);
  });

  it('pre-fills existing data in edit mode', () => {
    const [existing] = assembliesStore.getSnapshot();
    renderApp([`/assemblies/${existing.id}/edit`]);

    expect(screen.getByDisplayValue(existing.name)).toBeInTheDocument();
    expect(
      screen.getByText(`Parts · ${existing.components.length}`),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Save changes' }),
    ).toBeInTheDocument();
  });
});
