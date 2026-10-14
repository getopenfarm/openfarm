import { useQueryClient } from '@tanstack/react-query';
import type {
  CellContext,
  ColumnDef,
  HeaderContext,
  RowSelectionState,
} from '@tanstack/react-table';
import { Layers3Icon, Trash2Icon } from 'lucide-react';
import {
  createContext,
  type RefObject,
  useContext,
  useEffect,
  useRef,
  useState,
} from 'react';
import { Link } from 'react-router';
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
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  getOpenFarmApiEndpointsAssembliesListAssembliesEndpointQueryKey,
  useOpenFarmApiEndpointsAssembliesDeleteAssemblyEndpoint,
} from '@/lib/api/generated/assemblies/assemblies';
import type { AssemblySummaryResponse } from '@/lib/api/generated/endpoints.schemas';

interface AssembliesTableProps {
  assemblies: AssemblySummaryResponse[];
  pagination?: ServerTablePagination;
  sorting?: ServerTableSorting;
  onRowClick: (assembly: AssemblySummaryResponse) => void;
  selectionKey?: string;
  fallbackFocusRef?: RefObject<HTMLElement | null>;
}

interface RemovableAssembly {
  id: string;
  name: string;
}

const columns: ColumnDef<AssemblySummaryResponse>[] = [
  {
    id: 'assembly',
    accessorKey: 'name',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Assembly" />
    ),
    cell: ({ row }) => (
      <div className="flex items-center gap-3">
        <div className="flex size-10 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
          <Layers3Icon className="size-5" />
        </div>
        <div className="flex flex-col">
          <Link
            to={`/assemblies/${row.original.id}`}
            className="font-medium hover:underline"
          >
            {row.original.name}
          </Link>
          {row.original.description && (
            <span className="text-xs text-muted-foreground">
              {row.original.description}
            </span>
          )}
        </div>
      </div>
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
  pending: boolean;
  selectionHeaderRef: RefObject<HTMLButtonElement | null>;
  onRemove: (
    assembly: AssemblySummaryResponse,
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
}: HeaderContext<AssemblySummaryResponse, unknown>) {
  const { pending, selectionHeaderRef } = useAssembliesTableActions();
  return (
    <Checkbox
      ref={selectionHeaderRef}
      aria-label="Select all assemblies on this page"
      disabled={pending}
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
function SelectionCell({ row }: CellContext<AssemblySummaryResponse, unknown>) {
  const { pending } = useAssembliesTableActions();
  return (
    <Checkbox
      aria-label={`Select ${row.original.name}`}
      disabled={pending || !row.getCanSelect()}
      checked={row.getIsSelected()}
      onCheckedChange={(checked) => row.toggleSelected(checked === true)}
    />
  );
}
function ActionsCell({ row }: CellContext<AssemblySummaryResponse, unknown>) {
  const { pending, onRemove } = useAssembliesTableActions();
  const triggerRef = useRef<HTMLButtonElement>(null);
  return (
    <DataTableRowActions
      label={row.original.name ?? 'assembly'}
      triggerRef={triggerRef}
      disabled={pending || !row.original.id}
    >
      <DropdownMenuGroup>
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
const tableColumns: ColumnDef<AssemblySummaryResponse>[] = [
  {
    id: 'select',
    enableSorting: false,
    enableHiding: false,
    header: SelectionHeader,
    cell: SelectionCell,
  },
  ...columns,
  {
    id: 'actions',
    header: () => <span className="sr-only">Actions</span>,
    enableSorting: false,
    enableHiding: false,
    cell: ActionsCell,
  },
];

export function AssembliesTable({
  assemblies,
  pagination,
  sorting,
  onRowClick,
  selectionKey,
  fallbackFocusRef,
}: AssembliesTableProps) {
  const queryClient = useQueryClient();
  const deleteMutation =
    useOpenFarmApiEndpointsAssembliesDeleteAssemblyEndpoint({
      mutation: { retry: false },
    });
  const [selection, setSelection] = useState<RowSelectionState>({});
  const [targets, setTargets] = useState<RemovableAssembly[]>([]);
  const [bulk, setBulk] = useState(false);
  const [pending, setPending] = useState(false);
  const removalTriggerRef = useRef<HTMLButtonElement | null>(null);
  const selectionHeaderRef = useRef<HTMLButtonElement>(null);
  const bulkTriggerRef = useRef<HTMLButtonElement>(null);
  const interactionPending = !!(
    pending ||
    pagination?.pending ||
    sorting?.pending ||
    pagination?.disabled ||
    sorting?.disabled
  );

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
  const toRemovable = (a: AssemblySummaryResponse): RemovableAssembly[] =>
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
          `${target.name}: ${await extractErrorDetail(error, 'Could not remove this assembly.')}`,
        );
      }
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
        selectionHeaderRef,
        onRemove: (assembly, trigger) => {
          removalTriggerRef.current = trigger;
          setBulk(false);
          setTargets(toRemovable(assembly));
        },
      }}
    >
      <DataTableSelectionBar
        selectedCount={selected.length}
        pending={interactionPending}
        label="Selected assemblies actions"
        onClear={() => {
          setSelection({});
          selectionHeaderRef.current?.focus();
        }}
      >
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
      <DataTable
        serverSorting={sorting}
        serverPagination={
          pagination
            ? { ...pagination, pending: pending || pagination.pending }
            : undefined
        }
        columns={tableColumns}
        data={assemblies}
        onRowClick={(assembly) => {
          if (!interactionPending) onRowClick(assembly);
        }}
        getRowId={(assembly, index) => assembly.id ?? `missing-${index}`}
        rowSelection={selection}
        onRowSelectionChange={setSelection}
        enableRowSelection={(row) => Boolean(row.original.id)}
      />
      <ConfirmDialog
        open={targets.length > 0}
        onOpenChange={(open) => {
          if (!open) setTargets([]);
        }}
        returnFocusRef={removalTriggerRef}
        fallbackFocusRef={fallbackFocusRef}
        title={bulk ? 'Remove selected assemblies?' : 'Remove assembly?'}
        description={`Permanently remove ${targets.length === 1 ? targets[0]?.name : `${targets.length} assemblies`}? This cannot be undone.`}
        confirmLabel={
          pending
            ? 'Removing…'
            : `Remove ${targets.length === 1 ? 'assembly' : `${targets.length} assemblies`}`
        }
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
