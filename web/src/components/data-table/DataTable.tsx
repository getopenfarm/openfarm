import {
  type ColumnDef,
  flexRender,
  getCoreRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  type OnChangeFn,
  type PaginationState,
  type Row,
  type RowSelectionState,
  type SortingState,
  type TableOptions,
  useReactTable,
  type VisibilityState,
} from '@tanstack/react-table';
import type {
  ComponentType,
  HTMLAttributes,
  MouseEvent,
  ReactNode,
} from 'react';
import { useEffect, useRef, useState } from 'react';
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
import { DataTableViewOptions } from './DataTableViewOptions';

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

export interface DataTableRowContainerProps
  extends Pick<
    HTMLAttributes<HTMLElement>,
    'onClickCapture' | 'onKeyDown' | 'tabIndex' | 'aria-label'
  > {
  rowId: string;
  view: 'table' | 'grid';
  selected: boolean;
  className?: string;
  children: ReactNode;
  onClick?: (event: MouseEvent<HTMLElement>) => void;
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
  /** Left side of the framed toolbar; pass `null` to show only field visibility controls. */
  toolbar?: ReactNode;
  /** Compose trailing controls around the table-owned field visibility menu. */
  toolbarControls?: (fields: ReactNode) => ReactNode;
  /** Wrap rows/cards and pagination without wrapping the persistent toolbar. */
  renderContent?: (content: ReactNode) => ReactNode;
  showCardToolbar?: boolean;
  viewOptionsLabel?: string;
  columnVisibility?: VisibilityState;
  onColumnVisibilityChange?: OnChangeFn<VisibilityState>;
  viewOptionColumnIds?: string[];
  /** Alternate presentation sharing the same row, selection and pagination model. */
  rowContainer?: ComponentType<DataTableRowContainerProps>;
  renderCard?: (row: Row<TData>) => ReactNode;
  /** Library-only modifier selection; ordinary clicks retain navigation. */
  modifierSelection?: boolean;
  selectionDisabled?: boolean;
}

// 12px + 16px checkbox + 12px: equal space either side (overrides TableHead/TableCell's checkbox pr-0).
const selectColumnClassName = 'w-10 px-3 [&:has([role=checkbox])]:pr-3';

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
  toolbar,
  toolbarControls,
  renderContent,
  showCardToolbar = false,
  viewOptionsLabel = 'Columns',
  columnVisibility: controlledVisibility,
  onColumnVisibilityChange,
  viewOptionColumnIds,
  renderCard,
  modifierSelection = false,
  selectionDisabled = false,
  rowContainer: RowContainer = DefaultRowContainer,
}: DataTableProps<TData, TValue>) {
  const [sorting, setSorting] = useState<SortingState>([]);
  const [columnVisibility, setColumnVisibility] = useState<VisibilityState>({});

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
      columnVisibility: controlledVisibility ?? columnVisibility,
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
    onColumnVisibilityChange: onColumnVisibilityChange ?? setColumnVisibility,
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
  const anchor = useRef<string | null>(null);
  useEffect(() => {
    if (!Object.values(rowSelection ?? {}).some(Boolean)) anchor.current = null;
  }, [rowSelection]);
  const selectionBlocked =
    selectionDisabled ||
    pending ||
    serverSorting?.pending ||
    serverPagination?.disabled ||
    serverSorting?.disabled;
  function select(row: Row<TData>, range: boolean) {
    if (!row.getCanSelect()) return;
    const start = rows.findIndex((item) => item.id === anchor.current);
    if (range && start >= 0) {
      const end = rows.indexOf(row);
      table.setRowSelection(
        Object.fromEntries(
          rows
            .slice(Math.min(start, end), Math.max(start, end) + 1)
            .filter((item) => item.getCanSelect())
            .map((item) => [item.id, true]),
        ),
      );
    } else {
      row.toggleSelected();
      anchor.current = row.id;
    }
  }
  function interactionProps(
    row: Row<TData>,
  ): Partial<DataTableRowContainerProps> {
    if (!modifierSelection) return {};
    return {
      tabIndex: 0,
      'aria-label': row.getIsSelected()
        ? `${row.getValue('name')}, selected`
        : undefined,
      onClickCapture: (event) => {
        if (
          (event.target as Element).closest(
            'button, input, select, textarea, [role="checkbox"], [role="menuitem"]',
          )
        )
          return;
        if (selectionBlocked) {
          event.preventDefault();
          event.stopPropagation();
          return;
        }
        if (event.ctrlKey || event.metaKey || event.shiftKey) {
          event.preventDefault();
          event.stopPropagation();
          select(row, event.shiftKey);
        }
      },
      onKeyDown: (event) => {
        if (
          (event.target as Element).closest(
            'button, input, select, textarea, [role="menuitem"], [role="dialog"]',
          ) ||
          selectionBlocked
        )
          return;
        if (
          (event.ctrlKey || event.metaKey) &&
          event.key.toLowerCase() === 'a'
        ) {
          event.preventDefault();
          table.toggleAllPageRowsSelected(true);
        } else if (event.key === ' ') {
          event.preventDefault();
          select(row, event.shiftKey);
        } else if (event.key === 'Escape') {
          event.preventDefault();
          table.resetRowSelection();
        } else if (
          event.key === 'Enter' &&
          event.target === event.currentTarget
        ) {
          event.preventDefault();
          onRowClick?.(row.original);
        }
      },
    };
  }

  const content = (
    <>
      {renderCard ? (
        <>
          {!modifierSelection &&
            table
              .getAllLeafColumns()
              .some((column) => column.id === 'select') && (
              <div className="flex items-center gap-2 border-b p-3">
                {table
                  .getHeaderGroups()
                  .flatMap((group) => group.headers)
                  .filter((header) => header.column.id === 'select')
                  .map((header) => (
                    <div key={header.id}>
                      {flexRender(
                        header.column.columnDef.header,
                        header.getContext(),
                      )}
                    </div>
                  ))}
                <span className="text-sm text-muted-foreground">
                  Select items on this page
                </span>
              </div>
            )}
          <ul
            className={cn(
              'grid list-none',
              modifierSelection
                ? 'grid-cols-1 gap-3 p-3 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6'
                : 'grid-cols-1 gap-4 p-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4',
            )}
          >
            {rows.map((row) => (
              <RowContainer
                {...interactionProps(row)}
                view="grid"
                rowId={row.id}
                selected={row.getIsSelected()}
                key={row.id}
                className={cn(
                  'group/library-tile relative isolate min-w-0 rounded-xl outline-none focus-visible:ring-2 focus-visible:ring-ring data-[state=selected]:ring-2 data-[state=selected]:ring-primary',
                  onRowClick && !selectionBlocked && 'cursor-pointer',
                )}
                onClick={(event) => {
                  if (
                    !(event.target as Element).closest(
                      'a, button, input, [role="menuitem"]',
                    )
                  )
                    onRowClick?.(row.original);
                }}
              >
                {renderCard(row)}
              </RowContainer>
            ))}
          </ul>
          {rows.length === 0 && (
            <div className="p-4">{emptyState ?? 'No results'}</div>
          )}
        </>
      ) : (
        <Table>
          <TableHeader className="bg-muted/40">
            {table.getHeaderGroups().map((headerGroup) => (
              <TableRow key={headerGroup.id} className="hover:bg-transparent">
                {headerGroup.headers.map((header) => (
                  <TableHead
                    key={header.id}
                    // Utility columns hug their control instead of sharing slack width.
                    className={cn(
                      header.id === 'select' && selectColumnClassName,
                      header.id === 'actions' && 'w-px',
                    )}
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
                <RowContainer
                  {...interactionProps(row)}
                  view="table"
                  rowId={row.id}
                  selected={row.getIsSelected()}
                  key={row.id}
                  className={cn(
                    onRowClick && 'cursor-pointer',
                    modifierSelection &&
                      'outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring',
                  )}
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
                    <TableCell
                      key={cell.id}
                      className={cn(
                        cellClassName,
                        cell.column.id === 'select' && selectColumnClassName,
                      )}
                    >
                      {flexRender(
                        cell.column.columnDef.cell,
                        cell.getContext(),
                      )}
                    </TableCell>
                  ))}
                </RowContainer>
              ))
            ) : (
              <TableRow className="hover:bg-transparent">
                <TableCell
                  colSpan={table.getVisibleLeafColumns().length}
                  className="h-32 text-center text-sm text-muted-foreground"
                >
                  {emptyState ?? 'No results'}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      )}
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
    </>
  );
  const fields = (
    <DataTableViewOptions
      table={table}
      columnIds={viewOptionColumnIds}
      label={viewOptionsLabel}
      disabled={!!selectionBlocked}
    />
  );

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
          {pending || serverSorting?.pending ? 'Loading…' : ''}
        </span>
        {toolbar !== undefined && (!renderCard || showCardToolbar) ? (
          <div
            data-slot="table-toolbar-row"
            className="flex min-h-12 flex-wrap items-center gap-2 border-b px-3 py-2"
          >
            {toolbar}
            {toolbarControls ? (
              <div className="ml-auto flex max-w-full flex-wrap items-center justify-end gap-3">
                {toolbarControls(fields)}
              </div>
            ) : (
              fields
            )}
          </div>
        ) : renderCard ? (
          toolbar
        ) : null}
        {renderContent ? renderContent(content) : content}
      </div>
    </DataTableInteractionContext.Provider>
  );
}

function DefaultRowContainer({
  view,
  selected,
  rowId: _rowId,
  ...props
}: DataTableRowContainerProps) {
  return view === 'grid' ? (
    <li data-state={selected ? 'selected' : undefined} {...props} />
  ) : (
    <TableRow data-state={selected ? 'selected' : undefined} {...props} />
  );
}
