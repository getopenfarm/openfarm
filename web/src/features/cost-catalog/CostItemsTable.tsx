import { useQueryClient } from '@tanstack/react-query';
import type {
  CellContext,
  ColumnDef,
  HeaderContext,
  RowSelectionState,
} from '@tanstack/react-table';
import { PackageIcon, Trash2Icon } from 'lucide-react';
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
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import {
  DropdownMenuGroup,
  DropdownMenuItem,
} from '@/components/ui/dropdown-menu';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { CostItemResponse } from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
import { catalogKey } from './api';
import {
  categoryLabel,
  formatRate,
  packageQuantity,
  quantityLabel,
  unitLabels,
} from './costing';

interface CostItemsTableProps {
  items: CostItemResponse[];
  pagination?: ServerTablePagination;
  sorting?: ServerTableSorting;
  onRowClick: (item: CostItemResponse) => void;
  selectionKey?: string;
  fallbackFocusRef?: RefObject<HTMLElement | null>;
  onPendingChange?: (pending: boolean) => void;
}

interface RemovableCostItem {
  id: string;
  name: string;
}

const columns: ColumnDef<CostItemResponse>[] = [
  {
    id: 'name',
    accessorKey: 'name',
    meta: { label: 'Item' },
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Item" />
    ),
    cell: ({ row }) => {
      const item = row.original;
      return (
        <div className="flex items-center gap-3">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
            <PackageIcon className="size-5" />
          </div>
          <div className="flex min-w-0 flex-col gap-0.5">
            <Link
              to={`/cost-catalog/${item.id}`}
              className="font-medium hover:underline"
            >
              {item.name}
            </Link>
            <span className="text-xs text-muted-foreground">
              {item.pricing?.itemType === 'Service' ? 'Service' : 'Physical'}
            </span>
          </div>
        </div>
      );
    },
  },
  {
    id: 'category',
    header: 'Category',
    meta: { label: 'Category' },
    cell: ({ row }) => (
      <Badge variant="secondary">
        {categoryLabel(row.original.pricing?.category ?? 'Other')}
      </Badge>
    ),
  },
  {
    id: 'price',
    header: 'Price',
    meta: { label: 'Price' },
    cell: ({ row }) => {
      const pricing = row.original.pricing;
      const unit = pricing?.unit ?? 'Piece';
      const packaged = pricing?.pricingMode === 'Package';
      const price = packaged ? pricing?.packagePrice : pricing?.directRate;
      const quantity = pricing && packaged ? packageQuantity(pricing) : null;
      if (price == null)
        return <span className="text-muted-foreground">—</span>;
      return (
        <span className="tabular-nums">
          {quantity != null
            ? `${formatRate(price)} for ${quantityLabel(quantity, unit)}`
            : `${formatRate(price)} per ${unitLabels[unit]}`}
        </span>
      );
    },
  },
  {
    id: 'cost',
    header: () => <div className="text-right">Cost</div>,
    meta: { label: 'Cost' },
    cell: ({ row }) => (
      <div className="text-right">
        {row.original.effectiveRate == null ? (
          <Badge variant="outline">Not priced</Badge>
        ) : (
          <span className="whitespace-nowrap font-mono tabular-nums">
            {formatRate(row.original.effectiveRate)}{' '}
            <span className="text-muted-foreground">
              / {unitLabels[row.original.pricing?.unit ?? 'Piece']}
            </span>
          </span>
        )}
      </div>
    ),
  },
];

interface CostItemsTableActions {
  pending: boolean;
  selectionHeaderRef: RefObject<HTMLButtonElement | null>;
  onRemove: (item: CostItemResponse, trigger: HTMLButtonElement | null) => void;
}
const CostItemsTableContext = createContext<CostItemsTableActions | null>(null);
function useCostItemsTableActions() {
  const actions = useContext(CostItemsTableContext);
  if (!actions)
    throw new Error('Cost item table controls require CostItemsTableContext.');
  return actions;
}
function SelectionHeader({ table }: HeaderContext<CostItemResponse, unknown>) {
  const { pending, selectionHeaderRef } = useCostItemsTableActions();
  return (
    <Checkbox
      ref={selectionHeaderRef}
      aria-label="Select all cost items on this page"
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
function SelectionCell({ row }: CellContext<CostItemResponse, unknown>) {
  const { pending } = useCostItemsTableActions();
  return (
    <Checkbox
      aria-label={`Select ${row.original.name}`}
      disabled={pending || !row.getCanSelect()}
      checked={row.getIsSelected()}
      onCheckedChange={(checked) => row.toggleSelected(checked === true)}
    />
  );
}
function ActionsCell({ row }: CellContext<CostItemResponse, unknown>) {
  const { pending, onRemove } = useCostItemsTableActions();
  const triggerRef = useRef<HTMLButtonElement>(null);
  return (
    <DataTableRowActions
      label={row.original.name ?? 'item'}
      triggerRef={triggerRef}
      disabled={pending || !row.original.id}
    >
      <DropdownMenuGroup>
        <DropdownMenuItem asChild>
          <Link to={`/cost-catalog/${row.original.id}`}>Edit</Link>
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
const tableColumns: ColumnDef<CostItemResponse>[] = [
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

export function CostItemsTable({
  items,
  pagination,
  sorting,
  onRowClick,
  selectionKey,
  fallbackFocusRef,
  onPendingChange,
}: CostItemsTableProps) {
  const queryClient = useQueryClient();
  const [selection, setSelection] = useState<RowSelectionState>({});
  const [targets, setTargets] = useState<RemovableCostItem[]>([]);
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

  // Selection is page-local; preserve stable IDs through sorting and prune rows that leave the page.
  useEffect(() => {
    void selectionKey;
    setSelection({});
  }, [selectionKey]);
  useEffect(() => {
    const ids = new Set(items.map((item) => item.id));
    setSelection((previous) => {
      const entries = Object.entries(previous).filter(
        ([id, selected]) => selected && ids.has(id),
      );
      return entries.length === Object.keys(previous).length
        ? previous
        : Object.fromEntries(entries);
    });
  }, [items]);

  useEffect(() => {
    onPendingChange?.(pending);
  }, [pending, onPendingChange]);

  const selected = items.filter((a) => a.id && selection[a.id]);
  const toRemovable = (a: CostItemResponse): RemovableCostItem[] =>
    a.id ? [{ id: a.id, name: a.name ?? 'this item' }] : [];

  async function execute() {
    setPending(true);
    const removed: string[] = [];
    const failures = new Map<string, unknown>();
    try {
      for (const target of targets) {
        try {
          await httpClient(`${catalogKey}/${target.id}`, { method: 'DELETE' });
          removed.push(target.id);
        } catch (error) {
          if (error instanceof Response && error.status === 404)
            removed.push(target.id);
          else failures.set(target.id, error);
        }
      }
      if (removed.length) {
        removalTriggerRef.current =
          fallbackFocusRef?.current instanceof HTMLButtonElement
            ? fallbackFocusRef.current
            : selectionHeaderRef.current;
        setSelection((previous) =>
          Object.fromEntries(
            Object.entries(previous).filter(([id]) => !removed.includes(id)),
          ),
        );
        toast.success(
          removed.length === 1
            ? 'Cost item removed.'
            : `${removed.length} cost items removed.`,
        );
      }
      for (const target of targets) {
        const error = failures.get(target.id);
        if (error === undefined) continue;
        toast.error(
          `${target.name}: ${await extractErrorDetail(error, 'Removal failed.')}`,
        );
      }
      await queryClient.invalidateQueries({ queryKey: [catalogKey] });
      await queryClient.invalidateQueries({
        queryKey: ['/api/v1/non-printed-components'],
      });
    } finally {
      onPendingChange?.(false);
      setPending(false);
      setTargets([]);
      if (removed.length)
        requestAnimationFrame(() => fallbackFocusRef?.current?.focus());
    }
  }

  return (
    <CostItemsTableContext.Provider
      value={{
        pending: interactionPending,
        selectionHeaderRef,
        onRemove: (item, trigger) => {
          removalTriggerRef.current = trigger;
          setBulk(false);
          setTargets(toRemovable(item));
        },
      }}
    >
      <DataTable
        serverSorting={
          sorting
            ? { ...sorting, pending: pending || sorting.pending }
            : undefined
        }
        serverPagination={
          pagination
            ? { ...pagination, pending: pending || pagination.pending }
            : undefined
        }
        columns={tableColumns}
        data={items}
        onRowClick={(item) => {
          if (!interactionPending) onRowClick(item);
        }}
        getRowId={(item, index) => item.id ?? `missing-${index}`}
        rowSelection={selection}
        onRowSelectionChange={setSelection}
        enableRowSelection={(row) => Boolean(row.original.id)}
        toolbar={null}
      />
      <div
        className={
          selected.length
            ? 'fixed bottom-[max(1rem,env(safe-area-inset-bottom))] left-1/2 z-30 w-[calc(100%-2rem)] max-w-2xl -translate-x-1/2 rounded-xl border bg-background p-3 shadow-lg'
            : 'sr-only'
        }
      >
        <DataTableSelectionBar
          selectedCount={selected.length}
          pending={interactionPending}
          label="Cost items actions"
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
            <Trash2Icon data-icon="inline-start" />
            Remove selected
          </Button>
        </DataTableSelectionBar>
      </div>
      <ConfirmDialog
        open={targets.length > 0}
        onOpenChange={(open) => {
          if (!open) setTargets([]);
        }}
        returnFocusRef={removalTriggerRef}
        fallbackFocusRef={fallbackFocusRef}
        title={
          bulk ? `Remove ${targets.length} cost items?` : 'Remove cost item?'
        }
        description={targets.map((target) => target.name).join(', ')}
        confirmLabel={pending ? 'Removing…' : 'Remove'}
        destructive
        isPending={pending}
        closeOnConfirm={false}
        onConfirm={() => {
          void execute();
        }}
      />
    </CostItemsTableContext.Provider>
  );
}
