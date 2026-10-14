import {
  type ColumnDef,
  flexRender,
  getCoreRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  type OnChangeFn,
  type PaginationState,
  type RowSelectionState,
  type SortingState,
  type TableOptions,
  useReactTable,
} from '@tanstack/react-table';
import type { ReactNode } from 'react';
import { useEffect, useState } from 'react';
import { Separator } from '@/components/ui/separator';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { cn } from '@/lib/utils';
import { DataTableInteractionContext } from './DataTableInteractionContext';
import { DataTablePagination } from './DataTablePagination';

export interface ServerTablePagination {
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  onPageSizeChange?: (size: number) => void;
  label?: string;
  pending?: boolean;
  disabled?: boolean;
}

export interface ServerTableSorting {
  sorting: SortingState;
  onSortingChange: OnChangeFn<SortingState>;
  enableSortingRemoval?: boolean;
  pending?: boolean;
  disabled?: boolean;
}

interface DataTableProps<TData, TValue> {
  columns: ColumnDef<TData, TValue>[];
  data: TData[];
  onRowClick?: (row: TData) => void;
  getRowId?: TableOptions<TData>['getRowId'];
  rowSelection?: RowSelectionState;
  onRowSelectionChange?: OnChangeFn<RowSelectionState>;
  enableRowSelection?: TableOptions<TData>['enableRowSelection'];
  emptyState?: ReactNode;
  pageSize?: number;
  serverPagination?: ServerTablePagination;
  serverSorting?: ServerTableSorting;
  className?: string;
  /** Extra classes applied to every body cell — used to tune row density. */
  cellClassName?: string;
}

export function DataTable<TData, TValue>({
  columns,
  data,
  onRowClick,
  emptyState,
  pageSize,
  serverPagination,
  serverSorting,
  className,
  cellClassName,
  getRowId,
  rowSelection,
  onRowSelectionChange,
  enableRowSelection = false,
}: DataTableProps<TData, TValue>) {
  const [sorting, setSorting] = useState<SortingState>([]);

  const serverPage = serverPagination?.page;
  const serverPageSize = serverPagination?.pageSize;
  const totalCount = serverPagination?.totalCount;
  const changePage = serverPagination?.onPageChange;
  const pending = serverPagination?.pending;
  const recoveryBlocked = pending || serverPagination?.disabled;
  useEffect(() => {
    if (
      serverPage &&
      serverPageSize &&
      totalCount !== undefined &&
      !recoveryBlocked
    ) {
      const lastPage = Math.max(1, Math.ceil(totalCount / serverPageSize));
      if (serverPage > lastPage) changePage?.(lastPage);
    }
  }, [serverPage, serverPageSize, totalCount, changePage, recoveryBlocked]);

  const table = useReactTable({
    data,
    columns,
    state: {
      sorting: serverSorting?.sorting ?? sorting,
      ...(rowSelection ? { rowSelection } : {}),
      ...(serverPagination
        ? {
            pagination: {
              pageIndex: serverPagination.page - 1,
              pageSize: serverPagination.pageSize,
            },
          }
        : {}),
    },
    getRowId,
    onRowSelectionChange,
    enableRowSelection,
    onSortingChange: serverSorting?.onSortingChange ?? setSorting,
    enableSorting: !serverPagination || !!serverSorting,
    enableMultiSort: false,
    enableSortingRemoval: serverSorting?.enableSortingRemoval ?? true,
    manualSorting: !!serverPagination || !!serverSorting,
    getCoreRowModel: getCoreRowModel(),
    ...(!serverPagination && !serverSorting
      ? { getSortedRowModel: getSortedRowModel() }
      : {}),
    ...(serverPagination
      ? {
          manualPagination: true,
          rowCount: serverPagination.totalCount,
          onPaginationChange: (
            updater:
              | PaginationState
              | ((state: PaginationState) => PaginationState),
          ) => {
            const previous = {
              pageIndex: serverPagination.page - 1,
              pageSize: serverPagination.pageSize,
            };
            const next =
              typeof updater === 'function' ? updater(previous) : updater;
            serverPagination.onPageChange(next.pageIndex + 1);
          },
        }
      : {}),
    ...(!serverPagination && pageSize
      ? {
          getPaginationRowModel: getPaginationRowModel(),
          initialState: { pagination: { pageSize } },
        }
      : {}),
  });

  const rows = table.getRowModel().rows;

  return (
    <DataTableInteractionContext.Provider
      value={{
        sortingPending: !!(
          serverSorting?.pending ||
          pending ||
          serverSorting?.disabled ||
          serverPagination?.disabled
        ),
      }}
    >
      <div
        aria-busy={pending || serverSorting?.pending || undefined}
        className={cn('overflow-hidden rounded-md border', className)}
      >
        <span className="sr-only" aria-live="polite">
          {pending || serverSorting?.pending ? 'Loading rows.' : ''}
        </span>
        <Table>
          <TableHeader className="bg-muted/40">
            {table.getHeaderGroups().map((headerGroup) => (
              <TableRow key={headerGroup.id} className="hover:bg-transparent">
                {headerGroup.headers.map((header) => (
                  <TableHead
                    key={header.id}
                    aria-sort={
                      header.column.getCanSort() && header.column.getIsSorted()
                        ? header.column.getIsSorted() === 'asc'
                          ? 'ascending'
                          : 'descending'
                        : undefined
                    }
                  >
                    {header.isPlaceholder
                      ? null
                      : flexRender(
                          header.column.columnDef.header,
                          header.getContext(),
                        )}
                  </TableHead>
                ))}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {rows.length ? (
              rows.map((row) => (
                <TableRow
                  key={row.id}
                  data-state={row.getIsSelected() ? 'selected' : undefined}
                  className={cn(onRowClick && 'cursor-pointer')}
                  onClick={(event) => {
                    if (
                      (event.target as HTMLElement).closest(
                        'a, button, input, select, textarea, [role="button"], [role="checkbox"], [role="combobox"], [role="menuitem"]',
                      )
                    )
                      return;
                    onRowClick?.(row.original);
                  }}
                >
                  {row.getVisibleCells().map((cell) => (
                    <TableCell key={cell.id} className={cellClassName}>
                      {flexRender(
                        cell.column.columnDef.cell,
                        cell.getContext(),
                      )}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            ) : (
              <TableRow className="hover:bg-transparent">
                <TableCell
                  colSpan={columns.length}
                  className="h-32 text-center text-sm text-muted-foreground"
                >
                  {emptyState ?? 'No results.'}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
        {pageSize || serverPagination ? (
          <>
            <Separator />
            <DataTablePagination
              table={table}
              pending={
                pending || serverPagination?.disabled || serverSorting?.disabled
              }
              label={serverPagination?.label}
              onPageSizeChange={
                serverPagination ? serverPagination.onPageSizeChange : undefined
              }
            />
          </>
        ) : null}
      </div>
    </DataTableInteractionContext.Provider>
  );
}
