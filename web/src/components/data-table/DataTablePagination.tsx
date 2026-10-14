import type { Table } from '@tanstack/react-table';
import { TablePagination } from './TablePagination';

export function DataTablePagination<TData>({
  table,
  pending = false,
  onPageSizeChange,
  label,
}: {
  table: Table<TData>;
  pending?: boolean;
  onPageSizeChange?: (size: number) => void;
  label?: string;
}) {
  const { pageIndex, pageSize } = table.getState().pagination;
  return (
    <TablePagination
      label={label}
      page={pageIndex + 1}
      pageSize={pageSize}
      totalCount={table.getRowCount()}
      disabled={pending}
      onPageChange={(page) => table.setPageIndex(page - 1)}
      onPageSizeChange={
        onPageSizeChange ??
        (!table.options.manualPagination
          ? (size) => {
              table.setPageSize(size);
              table.setPageIndex(0);
            }
          : undefined)
      }
    />
  );
}
