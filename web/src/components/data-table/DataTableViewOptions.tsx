import type { RowData, Table } from '@tanstack/react-table';
import { Columns3Icon } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

declare module '@tanstack/react-table' {
  // biome-ignore lint/correctness/noUnusedVariables: generics must match TanStack's declaration.
  interface ColumnMeta<TData extends RowData, TValue> {
    /** Name shown in the Columns menu when `header` is not a plain string. */
    label?: string;
  }
}

/** Columns menu: toggles every hideable column except select/actions. */
export function DataTableViewOptions<TData>({
  table,
}: {
  table: Table<TData>;
}) {
  const columns = table
    .getAllLeafColumns()
    .filter(
      (column) =>
        column.getCanHide() &&
        column.id !== 'select' &&
        column.id !== 'actions',
    );
  if (!columns.length) return null;
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button type="button" variant="outline" size="sm" className="ml-auto">
          <Columns3Icon data-icon="inline-start" /> Columns
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-44">
        <DropdownMenuLabel>Columns</DropdownMenuLabel>
        <DropdownMenuSeparator />
        {columns.map((column) => {
          const header = column.columnDef.header;
          const label =
            column.columnDef.meta?.label ??
            (typeof header === 'string' ? header : column.id);
          return (
            <DropdownMenuCheckboxItem
              key={column.id}
              checked={column.getIsVisible()}
              onCheckedChange={(value) => column.toggleVisibility(!!value)}
              // Keep the menu open so several columns can be toggled in one go.
              onSelect={(event) => event.preventDefault()}
            >
              {label}
            </DropdownMenuCheckboxItem>
          );
        })}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
