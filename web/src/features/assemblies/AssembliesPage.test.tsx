import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router';
import { beforeEach, describe, expect, it } from 'vitest';
import { partsStore } from '@/features/parts/data/mockPartsStore';
import { AssembliesPage } from './AssembliesPage';
import { assembliesStore } from './data/mockAssembliesStore';
import { computeAssemblyTotals } from './lib/computeAssemblyTotals';

function renderApp(initialEntries: string[] = ['/assemblies']) {
  return render(
    <MemoryRouter initialEntries={initialEntries}>
      <Routes>
        <Route path="/assemblies" element={<AssembliesPage />} />
        <Route path="/assemblies/new" element={<div>New assembly page</div>} />
        <Route
          path="/assemblies/:id/edit"
          element={<div>Edit assembly page</div>}
        />
        <Route path="/jobs" element={<div>Jobs page</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('AssembliesPage', () => {
  beforeEach(() => {
    assembliesStore.reset();
    partsStore.reset();
  });

  it('renders the seeded assemblies with computed totals, including the nested sub-assembly one', () => {
    renderApp();

    const table = within(screen.getByRole('table'));
    expect(table.getByText('Desktop organizer kit')).toBeInTheDocument();
    expect(table.getByText('Hinged storage box')).toBeInTheDocument();
    expect(table.getByText('Keyboard set')).toBeInTheDocument();
    expect(table.getByText('Planter bundle')).toBeInTheDocument();

    const assemblies = assembliesStore.getSnapshot();
    const planterBundle = assemblies.find((a) => a.name === 'Planter bundle');
    if (!planterBundle) throw new Error('Planter bundle seed missing');
    const totals = computeAssemblyTotals(planterBundle, assemblies);

    // Planter bundle nests "Hinged storage box" ×3 plus a direct part — the
    // rendered "Total parts" cell must reflect the one-level-deep resolution,
    // not just the raw component count (2).
    expect(totals.totalParts).toBeGreaterThan(planterBundle.components.length);

    const row = table.getByText('Planter bundle').closest('tr');
    if (!row) throw new Error('Planter bundle row not found');
    expect(
      within(row).getByText(String(totals.totalParts)),
    ).toBeInTheDocument();
  });

  it('shows an empty state when there are no assemblies', () => {
    assembliesStore.setState([]);
    renderApp();

    expect(screen.getByText('No assemblies yet')).toBeInTheDocument();
    expect(
      screen.getByText('Create one to group parts into a bill of materials.'),
    ).toBeInTheDocument();
  });

  it('navigates to /assemblies/new when "New assembly" is clicked', async () => {
    const user = userEvent.setup();
    renderApp();

    await user.click(screen.getByRole('button', { name: 'New assembly' }));

    expect(screen.getByText('New assembly page')).toBeInTheDocument();
  });

  it('navigates to the edit page when a row is clicked', async () => {
    const user = userEvent.setup();
    renderApp();

    await user.click(screen.getByText('Desktop organizer kit'));

    expect(screen.getByText('Edit assembly page')).toBeInTheDocument();
  });

  it('queues a print and navigates straight to /jobs, without opening a dialog', async () => {
    const user = userEvent.setup();
    renderApp();

    const row = screen.getByText('Desktop organizer kit').closest('tr');
    if (!row) throw new Error('row not found');

    await user.click(within(row).getByRole('button', { name: 'Print' }));

    expect(screen.getByText('Jobs page')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
