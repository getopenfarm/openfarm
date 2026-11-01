import type { RowData, Table } from '@tanstack/react-table';
import { Columns3Icon } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

declare module '@tanstack/react-table' {
  interface ColumnMeta<TData extends RowData, TValue> {
    /** Name shown in the field visibility menu when `header` is not a plain string. */
    label?: string;
  }
}

/** Columns menu: toggles every hideable column except select/actions. */
export function DataTableViewOptions<TData>({
  table,
  columnIds,
  label = 'Columns',
  disabled = false,
}: {
  table: Table<TData>;
  columnIds?: string[];
  label?: string;
  disabled?: boolean;
}) {
  const columns = table
    .getAllLeafColumns()
    .filter(
      (column) =>
        column.getCanHide() &&
        (!columnIds || columnIds.includes(column.id)) &&
        column.id !== 'select' &&
        column.id !== 'actions',
    );
  if (!columns.length) return null;
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="ml-auto"
          disabled={disabled}
        >
          <Columns3Icon data-icon="inline-start" /> {label}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-44">
        <DropdownMenuLabel>{label}</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          {columns.map((column) => {
            const header = column.columnDef.header;
            const label =
              column.columnDef.meta?.label ??
              (typeof header === 'string' ? header : column.id);
            return (
              <DropdownMenuCheckboxItem
                key={column.id}
                checked={column.getIsVisible()}
                disabled={disabled}
                onCheckedChange={(value) => column.toggleVisibility(!!value)}
                // Keep the menu open so several columns can be toggled in one go.
                onSelect={(event) => event.preventDefault()}
              >
                {label}
              </DropdownMenuCheckboxItem>
            );
          })}
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
