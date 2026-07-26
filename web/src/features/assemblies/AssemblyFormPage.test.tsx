import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, Link, Outlet, RouterProvider } from 'react-router';
import { beforeEach, describe, expect, it } from 'vitest';
import { partsStore } from '@/features/assemblies/data/mockPartsStore';
import { AssemblyFormPage } from './AssemblyFormPage';
import { assembliesStore } from './data/mockAssembliesStore';

function TestLayout() {
  return (
    <div>
      <Link to="/assemblies">Nav away</Link>
      <Outlet />
    </div>
  );
}

function renderApp(initialEntries: string[]) {
  const router = createMemoryRouter(
    [
      {
        element: <TestLayout />,
        children: [
          {
            path: '/assemblies/new',
            element: <AssemblyFormPage mode="add" />,
          },
          {
            path: '/assemblies/:id/edit',
            element: <AssemblyFormPage mode="edit" />,
          },
          { path: '/assemblies', element: <div>Assemblies list page</div> },
        ],
      },
    ],
    { initialEntries },
  );
  return render(<RouterProvider router={router} />);
}

describe('AssemblyFormPage', () => {
  beforeEach(() => {
    assembliesStore.reset();
    partsStore.reset();
  });

  it('shows an inline field error when submitted with no name', async () => {
    const user = userEvent.setup();
    renderApp(['/assemblies/new']);

    await user.click(screen.getByRole('button', { name: 'Create assembly' }));

    expect(
      await screen.findByText('Give this assembly a name first.'),
    ).toBeInTheDocument();
  });

  it('adds a new assembly with a part component, shows a toast, and navigates to /assemblies', async () => {
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

    expect(await screen.findByText('Assemblies list page')).toBeInTheDocument();
    expect(assembliesStore.getSnapshot()).toHaveLength(before + 1);

    const created = assembliesStore
      .getSnapshot()
      .find((a) => a.name === 'Test assembly');
    expect(created).toBeDefined();
    expect(created?.components).toHaveLength(1);
  });

  it('does not commit anything to the store until Create is clicked, and warns on Cancel with unsaved changes', async () => {
    const user = userEvent.setup();
    const before = assembliesStore.getSnapshot().length;
    renderApp(['/assemblies/new']);

    await user.type(screen.getByLabelText('Assembly name'), 'Untitled draft');

    expect(assembliesStore.getSnapshot()).toHaveLength(before);

    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    const alertDialog = await screen.findByRole('alertdialog');
    expect(
      within(alertDialog).getByText('Discard unsaved changes?'),
    ).toBeInTheDocument();
    await user.click(
      within(alertDialog).getByRole('button', { name: 'Discard changes' }),
    );

    expect(await screen.findByText('Assemblies list page')).toBeInTheDocument();
    expect(assembliesStore.getSnapshot()).toHaveLength(before);
  });

  it('pre-fills existing data in edit mode', async () => {
    const [existing] = assembliesStore.getSnapshot();
    renderApp([`/assemblies/${existing.id}/edit`]);

    expect(await screen.findByDisplayValue(existing.name)).toBeInTheDocument();
    expect(
      screen.getByText(`Parts · ${existing.components.length}`),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Save changes' }),
    ).toBeInTheDocument();
  });

  it('blocks in-app navigation away while the form has unsaved changes', async () => {
    const user = userEvent.setup();
    renderApp(['/assemblies/new']);

    await user.type(screen.getByLabelText('Assembly name'), 'Dirty draft');

    await user.click(screen.getByRole('link', { name: 'Nav away' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(
      within(dialog).getByText('Discard unsaved changes?'),
    ).toBeInTheDocument();
  });
});
