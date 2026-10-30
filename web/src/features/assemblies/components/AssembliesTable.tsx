import { useQueryClient } from '@tanstack/react-query';
import type {
  CellContext,
  ColumnDef,
  HeaderContext,
  RowSelectionState,
} from '@tanstack/react-table';
import { flexRender } from '@tanstack/react-table';
import { FolderInputIcon, Trash2Icon } from 'lucide-react';
import {
  createContext,
  type ReactNode,
  type RefObject,
  useContext,
  useEffect,
  useRef,
  useState,
} from 'react';
import { toast } from 'sonner';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import {
  DataTable,
  type ServerTablePagination,
  type ServerTableSorting,
} from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { DataTableSelectionBar } from '@/components/data-table/DataTableSelectionBar';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import {
  DropdownMenuGroup,
  DropdownMenuItem,
} from '@/components/ui/dropdown-menu';
import { TabsContent } from '@/components/ui/tabs';
import type { AssemblyLibraryRow } from '@/features/library/api';
import { invalidateLibraryContents } from '@/features/library/api';
import {
  LibraryDragHandle,
  LibraryDragRow,
  useLibraryDragSelection,
} from '@/features/library/LibraryDnd';
import {
  LibraryFolderActions,
  LibraryFolderCard,
  LibraryFolderName,
} from '@/features/library/LibraryFolders';
import { LibraryItemCard } from '@/features/library/LibraryItemCard';
import { LibraryItemLink } from '@/features/library/LibraryItemLink';
import { LibraryPreview } from '@/features/library/LibraryPreview';
import { MoveToDialog } from '@/features/library/MoveToDialog';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey,
  useOpenFarmApiEndpointsAssembliesDeleteAssemblyEndpoint,
} from '@/lib/api/generated/assemblies/assemblies';
import { libraryFieldIds } from '@/lib/preferences/libraryFields';
import type { ViewMode } from '@/lib/preferences/types';
import { usePreferences } from '@/lib/preferences/usePreferences';

interface AssembliesTableProps {
  folderNavigation?: ReactNode;
  toolbarControls?: (fields: ReactNode) => ReactNode;
  dragging?: boolean;
  libraryRows?: AssemblyLibraryRow[];
  onPendingChange?: (pending: boolean) => void;
  view?: ViewMode;
  folderId?: string | null;
  assemblies: AssemblyLibraryRow[];
  pagination?: ServerTablePagination;
  sorting?: ServerTableSorting;
  onRowClick: (assembly: AssemblyLibraryRow) => void;
  selectionKey?: string;
  fallbackFocusRef?: RefObject<HTMLElement | null>;
}

interface RemovableAssembly {
  id: string;
  name: string;
}

const columns: ColumnDef<AssemblyLibraryRow>[] = [
  {
    id: 'name',
    enableHiding: false,
    accessorKey: 'name',
    meta: { label: 'Name' },
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Name" />
    ),
    cell: ({ row }) => (
      <div className="flex items-center gap-3">
        <LibraryDragHandle name={row.original.name ?? 'Assembly'} />
        <LibraryPreview kind="assembly" compact />
        <div className="flex flex-col">
          <LibraryItemLink library="assemblies" id={row.original.id}>
            {row.original.name}
          </LibraryItemLink>
        </div>
      </div>
    ),
  },
  {
    id: 'description',
    header: 'Description',
    cell: ({ row }) => (
      <span className="text-sm text-muted-foreground">
        {row.original.description || '—'}
      </span>
    ),
  },
  {
    id: 'lineCount',
    header: 'Lines',
    cell: ({ row }) => (
      <span className="text-sm text-muted-foreground">
        {row.original.lineCount} lines
      </span>
    ),
  },
];

interface AssembliesTableActions {
  onMove: (ids: string[], trigger: HTMLButtonElement | null) => void;
  pending: boolean;
  selectionHeaderRef: RefObject<HTMLButtonElement | null>;
  onRemove: (
    assembly: AssemblyLibraryRow,
    trigger: HTMLButtonElement | null,
  ) => void;
}
const AssembliesTableContext = createContext<AssembliesTableActions | null>(
  null,
);
function useAssembliesTableActions() {
  const actions = useContext(AssembliesTableContext);
  if (!actions)
    throw new Error('Assembly table controls require AssembliesTableContext.');
  return actions;
}
function SelectionHeader({
  table,
}: HeaderContext<AssemblyLibraryRow, unknown>) {
  const { pending, selectionHeaderRef } = useAssembliesTableActions();
  return (
    <Checkbox
      ref={selectionHeaderRef}
      aria-label="Select all assemblies on this page"
      disabled={
        pending || !table.getRowModel().rows.some((row) => row.getCanSelect())
      }
      checked={
        table.getIsAllPageRowsSelected() ||
        (table.getIsSomePageRowsSelected() && 'indeterminate')
      }
      onCheckedChange={(checked) =>
        table.toggleAllPageRowsSelected(checked === true)
      }
    />
  );
}
function SelectionCell({ row }: CellContext<AssemblyLibraryRow, unknown>) {
  const { pending } = useAssembliesTableActions();
  if (row.original.libraryFolder) return null;
  return (
    <Checkbox
      aria-label={`Select ${row.original.name}`}
      disabled={pending || !row.getCanSelect()}
      checked={row.getIsSelected()}
      onCheckedChange={(checked) => row.toggleSelected(checked === true)}
    />
  );
}
function ActionsCell({ row }: CellContext<AssemblyLibraryRow, unknown>) {
  const { pending, onRemove, onMove } = useAssembliesTableActions();
  const triggerRef = useRef<HTMLButtonElement>(null);
  if (row.original.libraryFolder)
    return <LibraryFolderActions folder={row.original.libraryFolder} />;
  return (
    <DataTableRowActions
      label={row.original.name ?? 'assembly'}
      triggerRef={triggerRef}
      disabled={pending || !row.original.id}
    >
      <DropdownMenuGroup>
        <DropdownMenuItem
          onSelect={() => row.toggleSelected()}
          disabled={pending}
        >
          {row.getIsSelected() ? 'Deselect' : 'Select'}
        </DropdownMenuItem>
        <DropdownMenuItem
          onSelect={() => {
            if (row.original.id) onMove([row.original.id], triggerRef.current);
          }}
        >
          <FolderInputIcon />
          Move to
        </DropdownMenuItem>
        <DropdownMenuItem
          variant="destructive"
          onSelect={() => onRemove(row.original, triggerRef.current)}
        >
          <Trash2Icon /> Remove
        </DropdownMenuItem>
      </DropdownMenuGroup>
    </DataTableRowActions>
  );
}

// Stable cell component identities keep open menus and dialogs mounted during selection/mutations.
const tableColumns: ColumnDef<AssemblyLibraryRow>[] = [
  {
    id: 'select',
    enableSorting: false,
    enableHiding: false,
    header: SelectionHeader,
    cell: SelectionCell,
  },
  ...columns.map((column) => ({
    ...column,
    cell: (context: CellContext<AssemblyLibraryRow, unknown>) =>
      context.row.original.libraryFolder ? (
        column.id === 'name' ? (
          <LibraryFolderName folder={context.row.original.libraryFolder} />
        ) : (
          <span className="text-muted-foreground">—</span>
        )
      ) : (
        flexRender(column.cell, context)
      ),
  })),
  {
    id: 'actions',
    header: () => <span className="sr-only">Actions</span>,
    enableSorting: false,
    enableHiding: false,
    cell: ActionsCell,
  },
];

export function AssembliesTable({
  folderNavigation,
  toolbarControls,
  dragging = false,
  libraryRows,
  onPendingChange,
  view = 'table',
  folderId = null,
  assemblies,
  pagination,
  sorting,
  onRowClick,
  selectionKey,
  fallbackFocusRef,
}: AssembliesTableProps) {
  const { getLibraryFields, setLibraryFields } = usePreferences();
  const queryClient = useQueryClient();
  const deleteMutation =
    useOpenFarmApiEndpointsAssembliesDeleteAssemblyEndpoint({
      mutation: { retry: false },
    });
  const [moveIds, setMoveIds] = useState<string[]>([]);
  const moveTriggerRef = useRef<HTMLButtonElement | null>(null);
  const moveBulkRef = useRef<HTMLButtonElement>(null);
  const [moving, setMoving] = useState(false);
  const [selection, setSelection] = useState<RowSelectionState>({});
  const [targets, setTargets] = useState<RemovableAssembly[]>([]);
  const [bulk, setBulk] = useState(false);
  const [pending, setPending] = useState(false);
  const removalTriggerRef = useRef<HTMLButtonElement | null>(null);
  const selectionHeaderRef = useRef<HTMLButtonElement>(null);
  const bulkTriggerRef = useRef<HTMLButtonElement>(null);
  const interactionPending = !!(
    pending ||
    moving ||
    pagination?.pending ||
    sorting?.pending ||
    pagination?.disabled ||
    sorting?.disabled
  );

  useEffect(() => {
    onPendingChange?.(pending || moving);
  }, [pending, moving, onPendingChange]);

  useLibraryDragSelection(selection, setSelection);

  // A new server page or sort has a separate selection context.
  useEffect(() => {
    void selectionKey;
    setSelection({});
  }, [selectionKey]);
  useEffect(() => {
    const ids = new Set(assemblies.map((assembly) => assembly.id));
    setSelection((previous) => {
      const entries = Object.entries(previous).filter(
        ([id, selected]) => selected && ids.has(id),
      );
      return entries.length === Object.keys(previous).length
        ? previous
        : Object.fromEntries(entries);
    });
  }, [assemblies]);

  const selected = assemblies.filter((a) => a.id && selection[a.id]);
  const toRemovable = (a: AssemblyLibraryRow): RemovableAssembly[] =>
    a.id ? [{ id: a.id, name: a.name ?? 'this assembly' }] : [];

  async function execute() {
    setPending(true);
    const removed: string[] = [];
    const failures = new Map<string, unknown>();
    let remaining = targets;
    try {
      // Retry 409s while a pass makes progress: removing a parent unblocks its sub-assemblies.
      while (remaining.length) {
        const blocked: RemovableAssembly[] = [];
        for (const target of remaining) {
          try {
            await deleteMutation.mutateAsync({ id: target.id });
            removed.push(target.id);
          } catch (error) {
            if (error instanceof Response && error.status === 404) {
              removed.push(target.id);
              continue;
            }
            failures.set(target.id, error);
            if (error instanceof Response && error.status === 409)
              blocked.push(target);
          }
        }
        if (blocked.length === remaining.length) break;
        for (const target of blocked) failures.delete(target.id);
        remaining = blocked;
      }
      if (removed.length) {
        setSelection((previous) =>
          Object.fromEntries(
            Object.entries(previous).filter(([id]) => !removed.includes(id)),
          ),
        );
        toast.success(
          removed.length === 1
            ? 'Assembly removed.'
            : `${removed.length} assemblies removed.`,
        );
      }
      for (const target of targets) {
        const error = failures.get(target.id);
        if (error === undefined) continue;
        toast.error(
          `${target.name}: ${await extractErrorDetail(error, 'Could not remove assembly.')}`,
        );
      }
      await invalidateLibraryContents(queryClient);
      await queryClient.invalidateQueries({
        queryKey:
          getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey(),
      });
    } finally {
      setPending(false);
      setTargets([]);
    }
  }

  return (
    <AssembliesTableContext.Provider
      value={{
        pending: interactionPending,
        onMove: (ids, trigger) => {
          moveTriggerRef.current = trigger;
          setMoveIds(ids);
        },
        selectionHeaderRef,
        onRemove: (assembly, trigger) => {
          removalTriggerRef.current = trigger;
          setBulk(false);
          setTargets(toRemovable(assembly));
        },
      }}
    >
      <DataTable
        toolbarControls={toolbarControls}
        renderContent={
          toolbarControls
            ? (content) => <TabsContent value={view}>{content}</TabsContent>
            : undefined
        }
        showCardToolbar
        viewOptionsLabel="Fields"
        columnVisibility={getLibraryFields('assemblies', view)}
        onColumnVisibilityChange={(updater) =>
          setLibraryFields('assemblies', view, updater)
        }
        viewOptionColumnIds={libraryFieldIds('assemblies', view)}
        modifierSelection={!!libraryRows}
        selectionDisabled={interactionPending}
        serverSorting={sorting}
        serverPagination={
          pagination
            ? { ...pagination, pending: pending || pagination.pending }
            : undefined
        }
        renderCard={
          view === 'grid'
            ? (row) =>
                row.original.libraryFolder ? (
                  <LibraryFolderCard folder={row.original.libraryFolder} />
                ) : (
                  <LibraryItemCard
                    row={row}
                    name={row.original.name ?? 'Assembly'}
                    to={`/assemblies/${row.original.id}`}
                    images={row.original.previewThumbnails ?? []}
                    assembly
                    disabled={interactionPending}
                  />
                )
            : undefined
        }
        columns={tableColumns}
        rowContainer={libraryRows ? LibraryDragRow : undefined}
        data={libraryRows ?? assemblies}
        onRowClick={(assembly) => {
          if (!interactionPending) onRowClick(assembly);
        }}
        getRowId={(assembly, index) =>
          assembly.libraryFolder
            ? `folder:${assembly.id}`
            : (assembly.id ?? `missing-${index}`)
        }
        rowSelection={selection}
        onRowSelectionChange={setSelection}
        enableRowSelection={(row) =>
          Boolean(row.original.id) && !row.original.libraryFolder
        }
        toolbar={
          <>
            {(selected.length === 0 || dragging) && folderNavigation}
            <div hidden={dragging} className={dragging ? 'hidden' : 'contents'}>
              <DataTableSelectionBar
                selectedCount={selected.length}
                pending={interactionPending}
                label="Assemblies actions"
                onClear={() => {
                  setSelection({});
                  (
                    selectionHeaderRef.current ?? fallbackFocusRef?.current
                  )?.focus();
                }}
              >
                <Button
                  ref={moveBulkRef}
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={interactionPending}
                  onClick={() => {
                    moveTriggerRef.current = moveBulkRef.current;
                    setMoveIds(
                      selected.flatMap((item) => (item.id ? [item.id] : [])),
                    );
                  }}
                >
                  <FolderInputIcon data-icon="inline-start" />
                  Move to
                </Button>
                <Button
                  ref={bulkTriggerRef}
                  type="button"
                  variant="destructive"
                  size="sm"
                  disabled={interactionPending}
                  onClick={() => {
                    removalTriggerRef.current = bulkTriggerRef.current;
                    setBulk(true);
                    setTargets(selected.flatMap(toRemovable));
                  }}
                >
                  <Trash2Icon data-icon="inline-start" /> Remove selected
                </Button>
              </DataTableSelectionBar>
            </div>
          </>
        }
      />
      {moveIds.length > 0 && (
        <MoveToDialog
          library="assemblies"
          ids={moveIds}
          initialParent={folderId}
          onClose={() => setMoveIds([])}
          onMoved={() => setSelection({})}
          returnFocusRef={moveTriggerRef}
          fallbackFocusRef={fallbackFocusRef}
          onPendingChange={(value) => {
            setMoving(value);
          }}
        />
      )}
      <ConfirmDialog
        open={targets.length > 0}
        onOpenChange={(open) => {
          if (!open) setTargets([]);
        }}
        returnFocusRef={removalTriggerRef}
        fallbackFocusRef={fallbackFocusRef}
        title={
          bulk ? `Remove ${targets.length} assemblies?` : 'Remove assembly?'
        }
        description={`Permanently remove ${targets.length === 1 ? targets[0]?.name : `${targets.length} assemblies`}? This cannot be undone.`}
        confirmLabel={pending ? 'Removing…' : 'Remove'}
        destructive
        isPending={pending}
        closeOnConfirm={false}
        onConfirm={() => {
          void execute();
        }}
      />
    </AssembliesTableContext.Provider>
  );
}
