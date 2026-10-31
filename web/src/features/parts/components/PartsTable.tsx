import type {
  CellContext,
  ColumnDef,
  HeaderContext,
  RowSelectionState,
} from '@tanstack/react-table';
import { flexRender } from '@tanstack/react-table';
import {
  FolderInputIcon,
  ImageOffIcon,
  ListPlusIcon,
  Trash2Icon,
} from 'lucide-react';
import {
  createContext,
  type ReactNode,
  type RefObject,
  useContext,
  useEffect,
  useRef,
  useState,
} from 'react';
import {
  DataTable,
  type ServerTablePagination,
  type ServerTableSorting,
} from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { DataTableSelectionBar } from '@/components/data-table/DataTableSelectionBar';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { PermissionMenuItem as DropdownMenuItem } from '@/components/PermissionMenuItem';
import { Badge } from '@/components/ui/badge';
import { Checkbox } from '@/components/ui/checkbox';
import {
  DropdownMenuGroup,
  DropdownMenuSeparator,
} from '@/components/ui/dropdown-menu';
import { TabsContent } from '@/components/ui/tabs';
import type { PartLibraryRow } from '@/features/library/api';
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
import { MoveToDialog } from '@/features/library/MoveToDialog';
import { SendToQueueDialog } from '@/features/production-runs/components/SendToQueueDialog';
import { useAllowed } from '@/lib/auth/AccessContext';
import { formatMm } from '@/lib/formatUnits';
import { libraryFieldIds } from '@/lib/preferences/libraryFields';
import type { ViewMode } from '@/lib/preferences/types';
import { usePreferences } from '@/lib/preferences/usePreferences';
import { formatPartFileType } from '../formatPartFileType';
import { PartEstimate } from './PartEstimate';
import {
  type PartRemovalNoticeData,
  PartRemovalToast,
} from './PartRemovalToast';
import { PartThumbnailMaterial } from './PartThumbnailMaterial';
import { RemovePartsDialog } from './RemovePartsDialog';
import type { RemovablePart } from './useRemoveParts';

interface PartsTableProps {
  folderNavigation?: ReactNode;
  toolbarControls?: (fields: ReactNode) => ReactNode;
  dragging?: boolean;
  libraryRows?: PartLibraryRow[];
  view?: ViewMode;
  folderId?: string | null;
  pagination?: ServerTablePagination;
  sorting?: ServerTableSorting;
  parts: PartLibraryRow[];
  onRowClick: (part: PartLibraryRow) => void;
  selectionKey?: string;
  fallbackFocusRef?: RefObject<HTMLElement | null>;
  onPendingChange?: (pending: boolean) => void;
}

function EstimateCell({ part }: { part: PartLibraryRow }) {
  return (
    <PartEstimate
      status={part.estimateStatus}
      minutes={part.estimatedPrintTimeMinutes}
      grams={part.estimatedFilamentWeightGrams}
    />
  );
}

const columns: ColumnDef<PartLibraryRow>[] = [
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
        <LibraryDragHandle name={row.original.name ?? 'Part'} />
        <div className="flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-md bg-muted text-muted-foreground">
          {row.original.thumbnailBase64 ? (
            <img
              draggable={false}
              src={`data:image/png;base64,${row.original.thumbnailBase64}`}
              alt=""
              className="size-full object-contain"
            />
          ) : (
            <ImageOffIcon className="size-4" />
          )}
        </div>
        <div className="flex flex-col">
          <LibraryItemLink library="parts" id={row.original.id}>
            {row.original.name}
          </LibraryItemLink>
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
  onMove,
  selected,
  onSelect,
}: {
  selected: boolean;
  onSelect: () => void;
  part: PartLibraryRow;
  disabled: boolean;
  onMove: (trigger: HTMLButtonElement | null) => void;
  onQueue: (trigger: HTMLButtonElement | null) => void;
  onRemove: (trigger: HTMLButtonElement | null) => void;
}) {
  const triggerRef = useRef<HTMLButtonElement>(null);
  const incomplete = part.printRequirementsStatus !== 'Complete';
  return (
    <DataTableRowActions
      permission="parts:delete|production-runs:create|library:manage"
      label={part.name ?? 'part'}
      triggerRef={triggerRef}
      disabled={disabled || !part.id}
    >
      <DropdownMenuGroup>
        <DropdownMenuItem onSelect={onSelect}>
          {selected ? 'Deselect' : 'Select'}
        </DropdownMenuItem>
        <DropdownMenuItem
          permission="production-runs:create"
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
        <DropdownMenuItem
          permission="library:manage"
          onSelect={() => onMove(triggerRef.current)}
        >
          <FolderInputIcon />
          Move to
        </DropdownMenuItem>
      </DropdownMenuGroup>
      <DropdownMenuSeparator />
      <DropdownMenuGroup>
        <DropdownMenuItem
          permission="parts:delete"
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
  onMove: (ids: string[], trigger: HTMLButtonElement | null) => void;
  pending: boolean;
  fallbackFocusRef?: RefObject<HTMLElement | null>;
  selectionHeaderRef: RefObject<HTMLButtonElement | null>;
  onQueue: (part: PartLibraryRow, trigger: HTMLButtonElement | null) => void;
  onRemove: (part: PartLibraryRow, trigger: HTMLButtonElement | null) => void;
  onPendingChange: (pending: boolean) => void;
}
const PartsTableContext = createContext<PartsTableActions | null>(null);
function usePartsTableActions() {
  const actions = useContext(PartsTableContext);
  if (!actions)
    throw new Error('Part table controls require PartsTableContext.');
  return actions;
}
function SelectionHeader({ table }: HeaderContext<PartLibraryRow, unknown>) {
  const { pending, selectionHeaderRef } = usePartsTableActions();
  return (
    <Checkbox
      ref={selectionHeaderRef}
      aria-label="Select all parts on this page"
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
function SelectionCell({ row }: CellContext<PartLibraryRow, unknown>) {
  const { pending } = usePartsTableActions();
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
function ActionsCell({ row }: CellContext<PartLibraryRow, unknown>) {
  const actions = usePartsTableActions();
  if (row.original.libraryFolder)
    return <LibraryFolderActions folder={row.original.libraryFolder} />;
  return (
    <PartRowActions
      selected={row.getIsSelected()}
      onSelect={() => row.toggleSelected()}
      part={row.original}
      onMove={(trigger) => {
        if (row.original.id) actions.onMove([row.original.id], trigger);
      }}
      disabled={actions.pending}
      onQueue={(trigger) => actions.onQueue(row.original, trigger)}
      onRemove={(trigger) => actions.onRemove(row.original, trigger)}
    />
  );
}

// Stable cell component identities keep open menus and dialogs mounted during selection/mutations.
const tableColumns: ColumnDef<PartLibraryRow>[] = [
  {
    id: 'select',
    enableSorting: false,
    enableHiding: false,
    header: SelectionHeader,
    cell: SelectionCell,
  },
  ...columns.map((column) => ({
    ...column,
    cell: (context: CellContext<PartLibraryRow, unknown>) =>
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

export function PartsTable({
  folderNavigation,
  toolbarControls,
  dragging = false,
  libraryRows,
  view = 'table',
  folderId = null,
  parts,
  pagination,
  sorting,
  onRowClick,
  selectionKey,
  fallbackFocusRef,
  onPendingChange,
}: PartsTableProps) {
  const { getLibraryFields, setLibraryFields } = usePreferences();
  const [queuePart, setQueuePart] = useState<PartLibraryRow | null>(null);
  const [moveIds, setMoveIds] = useState<string[]>([]);
  const moveTriggerRef = useRef<HTMLButtonElement | null>(null);
  const moveBulkRef = useRef<HTMLButtonElement>(null);
  const [moving, setMoving] = useState(false);
  const canSelect = useAllowed(
    'parts:delete|production-runs:create|library:manage',
  );
  const [selection, setSelection] = useState<RowSelectionState>({});
  useEffect(() => {
    if (!canSelect) setSelection({});
  }, [canSelect]);
  const [bulkTargets, setBulkTargets] = useState<RemovablePart[]>([]);
  const [removalNotice, setRemovalNotice] = useState<PartRemovalNoticeData>();
  const [removalBulk, setRemovalBulk] = useState(false);
  const removalTriggerRef = useRef<HTMLButtonElement | null>(null);
  const [bulkOpen, setBulkOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const interactionPending = !!(
    pending ||
    moving ||
    pagination?.pending ||
    sorting?.pending ||
    pagination?.disabled ||
    sorting?.disabled
  );
  const queueTriggerRef = useRef<HTMLButtonElement | null>(null);
  const selectionHeaderRef = useRef<HTMLButtonElement>(null);
  const bulkTriggerRef = useRef<HTMLButtonElement>(null);

  useLibraryDragSelection(selection, setSelection);

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
        onMove: (ids, trigger) => {
          moveTriggerRef.current = trigger;
          setMoveIds(ids);
        },
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
          toolbarControls={toolbarControls}
          renderContent={
            toolbarControls
              ? (content) => <TabsContent value={view}>{content}</TabsContent>
              : undefined
          }
          showCardToolbar
          viewOptionsLabel="Fields"
          columnVisibility={getLibraryFields('parts', view)}
          onColumnVisibilityChange={(updater) =>
            setLibraryFields('parts', view, updater)
          }
          viewOptionColumnIds={libraryFieldIds('parts', view)}
          modifierSelection={!!libraryRows && canSelect}
          selectionDisabled={interactionPending || !canSelect}
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
                      name={row.original.name ?? 'Part'}
                      to={`/parts/${row.original.id}`}
                      images={
                        row.original.thumbnailBase64
                          ? [row.original.thumbnailBase64]
                          : []
                      }
                      fileTypeChip={
                        row
                          .getAllCells()
                          .some(
                            (c) =>
                              c.column.id === 'fileType' &&
                              c.column.getIsVisible(),
                          ) ? (
                          <Badge variant="secondary">
                            {formatPartFileType(row.original)}
                          </Badge>
                        ) : undefined
                      }
                      materialChip={
                        row
                          .getAllCells()
                          .some(
                            (c) =>
                              c.column.id === 'material' &&
                              c.column.getIsVisible(),
                          ) ? (
                          <PartThumbnailMaterial
                            part={row.original}
                            disabled={interactionPending}
                          />
                        ) : undefined
                      }
                      disabled={interactionPending}
                    />
                  )
              : undefined
          }
          columns={
            canSelect
              ? tableColumns
              : tableColumns.filter((column) => column.id !== 'select')
          }
          rowContainer={libraryRows ? LibraryDragRow : undefined}
          data={libraryRows ?? parts}
          onRowClick={(part) => {
            if (!interactionPending) onRowClick(part);
          }}
          getRowId={(part, index) =>
            part.libraryFolder
              ? `folder:${part.id}`
              : (part.id ?? `missing-${index}`)
          }
          rowSelection={selection}
          onRowSelectionChange={setSelection}
          enableRowSelection={(row) =>
            canSelect && Boolean(row.original.id) && !row.original.libraryFolder
          }
          toolbar={
            <>
              {(selected.length === 0 || dragging) && folderNavigation}
              <div
                hidden={dragging}
                className={dragging ? 'hidden' : 'contents'}
              >
                <DataTableSelectionBar
                  selectedCount={selected.length}
                  pending={interactionPending}
                  label="Parts actions"
                  onClear={() => {
                    setSelection({});
                    setRemovalNotice(undefined);
                    (
                      selectionHeaderRef.current ?? fallbackFocusRef?.current
                    )?.focus();
                  }}
                >
                  <Button
                    permission="library:manage"
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
                    permission="parts:delete"
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
              </div>
            </>
          }
        />
      </div>
      {moveIds.length > 0 && (
        <MoveToDialog
          library="parts"
          ids={moveIds}
          initialParent={folderId}
          onClose={() => setMoveIds([])}
          onMoved={() => setSelection({})}
          returnFocusRef={moveTriggerRef}
          fallbackFocusRef={fallbackFocusRef}
          onPendingChange={(value) => {
            setMoving(value);
            onPendingChange?.(value);
          }}
        />
      )}
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
        <SendToQueueDialog
          key={queuePart.id}
          target={{
            kind: 'Part',
            id: queuePart.id ?? '',
            name: queuePart.name ?? '',
          }}
          returnFocusRef={queueTriggerRef}
          onClose={() => setQueuePart(null)}
        />
      )}
    </PartsTableContext.Provider>
  );
}
