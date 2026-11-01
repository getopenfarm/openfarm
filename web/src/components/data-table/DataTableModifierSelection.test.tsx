import type { RowSelectionState } from '@tanstack/react-table';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { Link, MemoryRouter } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { DataTable } from './DataTable';

const data = [
  { id: 'a', name: 'Bracket', folder: false },
  { id: 'f', name: 'Folder', folder: true },
  { id: 'b', name: 'Clip', folder: false },
  { id: 'c', name: 'Lid', folder: false },
];
function setup(view: 'grid' | 'table', disabled = false) {
  const open = vi.fn();
  const action = vi.fn();
  function Harness() {
    const [selection, setSelection] = useState<RowSelectionState>({});
    return (
      <>
        <output aria-label="Selection">
          {Object.keys(selection)
            .filter((id) => selection[id])
            .sort()
            .join(',')}
        </output>
        <DataTable
          data={data}
          getRowId={(item) => item.id}
          columns={[
            {
              accessorKey: 'name',
              header: 'Name',
              cell: ({ row }) => (
                <Link to={`/${row.id}`}>{row.original.name}</Link>
              ),
            },
            {
              id: 'actions',
              cell: () => (
                <button type="button" onClick={action}>
                  Action
                </button>
              ),
            },
          ]}
          modifierSelection
          selectionDisabled={disabled}
          rowSelection={selection}
          onRowSelectionChange={setSelection}
          enableRowSelection={(row) => !row.original.folder}
          onRowClick={open}
          renderCard={
            view === 'grid'
              ? (row) => (
                  <>
                    <Link to={`/${row.id}`}>{row.original.name}</Link>
                    <button type="button" onClick={action}>
                      Action
                    </button>
                  </>
                )
              : undefined
          }
        />
      </>
    );
  }
  render(
    <MemoryRouter>
      <Harness />
    </MemoryRouter>,
  );
  return {
    open,
    action,
    user: userEvent.setup(),
    row: (name: string) =>
      screen
        .getByRole('link', { name })
        .closest(view === 'grid' ? 'li' : 'tr') as HTMLElement,
  };
}
describe.each(['grid', 'table'] as const)(
  'Library modifier selection in %s view',
  (view) => {
    it('toggles selection from links and surfaces, selects ranges excluding folders, and keeps nested actions separate', async () => {
      const { open, action, row } = setup(view);
      fireEvent.click(screen.getByRole('link', { name: 'Bracket' }), {
        ctrlKey: true,
      });
      expect(screen.getByLabelText('Selection')).toHaveTextContent('a');
      fireEvent.click(row('Lid'), { shiftKey: true });
      expect(screen.getByLabelText('Selection')).toHaveTextContent('a,b,c');
      fireEvent.click(screen.getByRole('link', { name: 'Clip' }), {
        metaKey: true,
      });
      expect(screen.getByLabelText('Selection')).toHaveTextContent('a,c');
      fireEvent.click(screen.getByRole('link', { name: 'Folder' }), {
        ctrlKey: true,
      });
      expect(screen.getByLabelText('Selection')).toHaveTextContent('a,c');
      fireEvent.click(screen.getAllByRole('button', { name: 'Action' })[0], {
        ctrlKey: true,
      });
      expect(action).toHaveBeenCalledOnce();
      expect(open).not.toHaveBeenCalled();
      fireEvent.click(row('Lid'));
      expect(open).toHaveBeenCalledWith(data[3]);
    });
    it('supports focused keyboard selection, select-all, clearing and opening', async () => {
      const { user, open, row } = setup(view);
      row('Bracket').focus();
      await user.keyboard(' ');
      expect(screen.getByLabelText('Selection')).toHaveTextContent('a');
      row('Lid').focus();
      await user.keyboard('{Shift>} {/Shift}');
      expect(screen.getByLabelText('Selection')).toHaveTextContent('a,b,c');
      await user.keyboard('{Escape}');
      expect(screen.getByLabelText('Selection')).toBeEmptyDOMElement();
      expect(row('Lid')).toHaveFocus();
      await user.keyboard('{Control>}a{/Control}');
      expect(screen.getByLabelText('Selection')).toHaveTextContent('a,b,c');
      await user.keyboard('{Enter}');
      expect(open).toHaveBeenCalledWith(data[3]);
    });
    it('blocks selection and navigation while a request is pending', () => {
      const { open, row } = setup(view, true);
      fireEvent.click(row('Bracket'), { ctrlKey: true });
      fireEvent.click(row('Bracket'));
      fireEvent.keyDown(row('Bracket'), { key: ' ', ctrlKey: true });
      expect(screen.getByLabelText('Selection')).toBeEmptyDOMElement();
      expect(open).not.toHaveBeenCalled();
    });
  },
);
