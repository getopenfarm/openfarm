import type {
  CellContext,
  ColumnDef,
  HeaderContext,
  RowSelectionState,
} from '@tanstack/react-table';
import { ImageOffIcon, ListPlusIcon, Trash2Icon } from 'lucide-react';
import {
  createContext,
  type RefObject,
  useContext,
  useEffect,
  useRef,
  useState,
} from 'react';
import { Link } from 'react-router';
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
  DropdownMenuSeparator,
} from '@/components/ui/dropdown-menu';
import type { PartSummaryResponse } from '@/lib/api/generated/endpoints.schemas';
import { formatMm } from '@/lib/formatUnits';
import { formatPartFileType } from '../formatPartFileType';

import { PartEstimate } from './PartEstimate';
import {
  type PartRemovalNoticeData,
  PartRemovalToast,
} from './PartRemovalToast';
import { QueuePartDialog } from './QueuePartDialog';
import { RemovePartsDialog } from './RemovePartsDialog';
import type { RemovablePart } from './useRemoveParts';

interface PartsTableProps {
  pagination?: ServerTablePagination;
  sorting?: ServerTableSorting;
  parts: PartSummaryResponse[];
  onRowClick: (part: PartSummaryResponse) => void;
  selectionKey?: string;
  fallbackFocusRef?: RefObject<HTMLElement | null>;
  onPendingChange?: (pending: boolean) => void;
}

function EstimateCell({ part }: { part: PartSummaryResponse }) {
  return (
    <PartEstimate
      status={part.estimateStatus}
      minutes={part.estimatedPrintTimeMinutes}
      grams={part.estimatedFilamentWeightGrams}
    />
  );
}

const columns: ColumnDef<PartSummaryResponse>[] = [
  {
    id: 'part',
    accessorKey: 'name',
    meta: { label: 'Part' },
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Part" />
    ),
    cell: ({ row }) => (
      <div className="flex items-center gap-3">
        <div className="flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-md bg-muted text-muted-foreground">
          {row.original.thumbnailBase64 ? (
            <img
              src={`data:image/png;base64,${row.original.thumbnailBase64}`}
              alt=""
              className="size-full object-contain"
            />
          ) : (
            <ImageOffIcon className="size-4" />
          )}
        </div>
        <div className="flex flex-col">
          <Link
            to={`/parts/${row.original.id}`}
            className="font-medium hover:underline"
          >
            {row.original.name}
          </Link>
        </div>
      </div>
    ),
  },
  {
    id: 'fileType',
    header: 'File type',
    enableSorting: false,
    cell: ({ row }) => (
      <Badge variant="secondary">{formatPartFileType(row.original)}</Badge>
    ),
  },
  {
    id: 'material',
    header: 'Material',
    cell: ({ row }) => {
      const materialTypes = row.original.materialTypes ?? [];
      if (materialTypes.length === 0) {
        return (
          <Badge variant="secondary" className="font-normal">
            {row.original.requiresSlicing ? 'Any material' : 'Unknown material'}
          </Badge>
        );
      }
      return (
        <div className="flex flex-wrap gap-1">
          {materialTypes.map((material) => (
            <Badge key={material} variant="secondary" className="font-normal">
              {material}
            </Badge>
          ))}
        </div>
      );
    },
  },
  {
    id: 'nozzleDiameter',
    header: 'Nozzle',
    cell: ({ row }) => (
      <span className="font-mono text-sm">
        {row.original.nozzleDiameterMm != null
          ? formatMm(row.original.nozzleDiameterMm)
          : 'Unknown'}
      </span>
    ),
  },
  {
    id: 'estimate',
    header: 'Estimate',
    cell: ({ row }) => <EstimateCell part={row.original} />,
  },
  {
    id: 'tags',
    header: 'Tags',
    cell: ({ row }) => (
      <div className="flex flex-wrap gap-1">
        {(row.original.tags ?? []).map((tag) => (
          <Badge key={tag} variant="outline" className="font-normal">
            {tag}
          </Badge>
        ))}
      </div>
    ),
  },
];

function PartRowActions({
  part,
  disabled,
  onQueue,
  onRemove,
}: {
  part: PartSummaryResponse;
  disabled: boolean;
  onQueue: (trigger: HTMLButtonElement | null) => void;
  onRemove: (trigger: HTMLButtonElement | null) => void;
}) {
  const triggerRef = useRef<HTMLButtonElement>(null);
  const incomplete = part.printRequirementsStatus !== 'Complete';
  return (
    <DataTableRowActions
      label={part.name ?? 'part'}
      triggerRef={triggerRef}
      disabled={disabled || !part.id}
    >
      <DropdownMenuGroup>
        <DropdownMenuItem
          disabled={incomplete}
          title={
            incomplete
              ? 'Complete this part’s print requirements before queueing.'
              : undefined
          }
          onSelect={() => onQueue(triggerRef.current)}
        >
          <ListPlusIcon /> Send to queue
        </DropdownMenuItem>
      </DropdownMenuGroup>
      <DropdownMenuSeparator />
      <DropdownMenuGroup>
        <DropdownMenuItem
          variant="destructive"
          onSelect={() => onRemove(triggerRef.current)}
        >
          <Trash2Icon /> Remove
        </DropdownMenuItem>
      </DropdownMenuGroup>
    </DataTableRowActions>
  );
}

interface PartsTableActions {
  pending: boolean;
  fallbackFocusRef?: RefObject<HTMLElement | null>;
  selectionHeaderRef: RefObject<HTMLButtonElement | null>;
  onQueue: (
    part: PartSummaryResponse,
    trigger: HTMLButtonElement | null,
  ) => void;
  onRemove: (
    part: PartSummaryResponse,
    trigger: HTMLButtonElement | null,
  ) => void;
  onPendingChange: (pending: boolean) => void;
}
const PartsTableContext = createContext<PartsTableActions | null>(null);
function usePartsTableActions() {
  const actions = useContext(PartsTableContext);
  if (!actions)
    throw new Error('Part table controls require PartsTableContext.');
  return actions;
}
function SelectionHeader({
  table,
}: HeaderContext<PartSummaryResponse, unknown>) {
  const { pending, selectionHeaderRef } = usePartsTableActions();
  return (
    <Checkbox
      ref={selectionHeaderRef}
      aria-label="Select all parts on this page"
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
function SelectionCell({ row }: CellContext<PartSummaryResponse, unknown>) {
  const { pending } = usePartsTableActions();
  return (
    <Checkbox
      aria-label={`Select ${row.original.name}`}
      disabled={pending || !row.getCanSelect()}
      checked={row.getIsSelected()}
      onCheckedChange={(checked) => row.toggleSelected(checked === true)}
    />
  );
}
function ActionsCell({ row }: CellContext<PartSummaryResponse, unknown>) {
  const actions = usePartsTableActions();
  return (
    <PartRowActions
      part={row.original}
      disabled={actions.pending}
      onQueue={(trigger) => actions.onQueue(row.original, trigger)}
      onRemove={(trigger) => actions.onRemove(row.original, trigger)}
    />
  );
}

// Stable cell component identities keep open menus and dialogs mounted during selection/mutations.
const tableColumns: ColumnDef<PartSummaryResponse>[] = [
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

export function PartsTable({
  parts,
  pagination,
  sorting,
  onRowClick,
  selectionKey,
  fallbackFocusRef,
  onPendingChange,
}: PartsTableProps) {
  const [queuePart, setQueuePart] = useState<PartSummaryResponse | null>(null);
  const [selection, setSelection] = useState<RowSelectionState>({});
  const [bulkTargets, setBulkTargets] = useState<RemovablePart[]>([]);
  const [removalNotice, setRemovalNotice] = useState<PartRemovalNoticeData>();
  const [removalBulk, setRemovalBulk] = useState(false);
  const removalTriggerRef = useRef<HTMLButtonElement | null>(null);
  const [bulkOpen, setBulkOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const interactionPending = !!(
    pending ||
    pagination?.pending ||
    sorting?.pending ||
    pagination?.disabled ||
    sorting?.disabled
  );
  const queueTriggerRef = useRef<HTMLButtonElement | null>(null);
  const selectionHeaderRef = useRef<HTMLButtonElement>(null);
  const bulkTriggerRef = useRef<HTMLButtonElement>(null);

  // A new server page or filter has a separate selection context.
  useEffect(() => {
    void selectionKey;
    setSelection({});
    setRemovalNotice(undefined);
  }, [selectionKey]);
  useEffect(() => {
    const ids = new Set(parts.map((part) => part.id));
    setSelection((previous) => {
      const entries = Object.entries(previous).filter(
        ([id, selected]) => selected && ids.has(id),
      );
      return entries.length === Object.keys(previous).length
        ? previous
        : Object.fromEntries(entries);
    });
  }, [parts]);

  const selected = parts.filter((part) => part.id && selection[part.id]);
  function handlePendingChange(value: boolean) {
    setPending(value);
    onPendingChange?.(value);
  }
  function handleRemoved(ids: string[]) {
    setSelection((previous) =>
      Object.fromEntries(
        Object.entries(previous).filter(([id]) => !ids.includes(id)),
      ),
    );
  }
  return (
    <PartsTableContext.Provider
      value={{
        pending: interactionPending,
        fallbackFocusRef,
        selectionHeaderRef,
        onQueue: (part, trigger) => {
          queueTriggerRef.current = trigger;
          setQueuePart(part);
        },
        onRemove: (part, trigger) => {
          if (!part.id) return;
          removalTriggerRef.current = trigger;
          setBulkTargets([{ id: part.id, name: part.name ?? 'this part' }]);
          setRemovalBulk(false);
          setBulkOpen(true);
        },
        onPendingChange: handlePendingChange,
      }}
    >
      <PartRemovalToast notice={removalNotice} />
      <div
        className="flex flex-col"
        onClickCapture={(event) => {
          if (pending) {
            event.preventDefault();
            event.stopPropagation();
          }
        }}
      >
        <DataTable
          serverSorting={sorting}
          serverPagination={
            pagination
              ? { ...pagination, pending: pending || pagination.pending }
              : undefined
          }
          columns={tableColumns}
          data={parts}
          onRowClick={(part) => {
            if (!interactionPending) onRowClick(part);
          }}
          getRowId={(part, index) => part.id ?? `missing-${index}`}
          rowSelection={selection}
          onRowSelectionChange={setSelection}
          enableRowSelection={(row) => Boolean(row.original.id)}
          toolbar={
            <DataTableSelectionBar
              selectedCount={selected.length}
              pending={interactionPending}
              label="Parts actions"
              onClear={() => {
                setSelection({});
                setRemovalNotice(undefined);
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
                  setRemovalBulk(true);
                  setBulkTargets(
                    selected.flatMap((part) =>
                      part.id
                        ? [{ id: part.id, name: part.name ?? 'this part' }]
                        : [],
                    ),
                  );
                  setBulkOpen(true);
                }}
              >
                <Trash2Icon data-icon="inline-start" /> Remove selected
              </Button>
            </DataTableSelectionBar>
          }
        />
      </div>
      {bulkOpen && (
        <RemovePartsDialog
          onNotice={setRemovalNotice}
          parts={bulkTargets}
          bulk={removalBulk}
          open={bulkOpen}
          onOpenChange={setBulkOpen}
          onRemoved={handleRemoved}
          onPendingChange={handlePendingChange}
          returnFocusRef={removalTriggerRef}
          fallbackFocusRef={fallbackFocusRef}
        />
      )}
      {queuePart && (
        <QueuePartDialog
          key={queuePart.id}
          part={queuePart}
          returnFocusRef={queueTriggerRef}
          onClose={() => setQueuePart(null)}
        />
      )}
    </PartsTableContext.Provider>
  );
}
