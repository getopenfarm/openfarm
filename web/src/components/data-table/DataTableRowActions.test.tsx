import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { DropdownMenuItem } from '@/components/ui/dropdown-menu';
import { DataTableRowActions } from './DataTableRowActions';

function Row() {
  return (
    <table>
      <tbody>
        <tr>
          <td>Bracket</td>
          <td>
            <a href="/parts/1">Open</a>
          </td>
          <td>
            <DataTableRowActions label="Bracket">
              <DropdownMenuItem>Edit</DropdownMenuItem>
            </DataTableRowActions>
          </td>
        </tr>
      </tbody>
    </table>
  );
}

describe('DataTableRowActions', () => {
  it('opens the row menu on right-click, but keeps the native menu on links', () => {
    render(<Row />);

    const onLink = fireEvent.contextMenu(screen.getByText('Open'));
    expect(onLink).toBe(true);
    expect(screen.queryByRole('menuitem', { name: 'Edit' })).toBeNull();

    const onRow = fireEvent.contextMenu(screen.getByText('Bracket'));
    expect(onRow).toBe(false);
    expect(screen.getByRole('menuitem', { name: 'Edit' })).toBeVisible();
  });
});
