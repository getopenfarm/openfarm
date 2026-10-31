import type { ColumnDef, RowSelectionState } from '@tanstack/react-table';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import {
  DataTable,
  type ServerTablePagination,
} from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { DataTableSelectionBar } from '@/components/data-table/DataTableSelectionBar';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { PermissionMenuItem as DropdownMenuItem } from '@/components/PermissionMenuItem';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Swatch } from '@/components/swatch/Swatch';
import { Checkbox } from '@/components/ui/checkbox';
import { DropdownMenuGroup } from '@/components/ui/dropdown-menu';
import { Progress } from '@/components/ui/progress';
import type { SpoolResponse } from '@/lib/api/generated/endpoints.schemas';
import { useAllowed } from '@/lib/auth/AccessContext';
import { formatGrams } from '@/lib/formatUnits';
import { useRestoreSpool } from '../hooks/useRestoreSpool';
import { FinishSpoolsDialog, type FinishTarget } from './FinishSpoolsDialog';
import { LoadSpoolDialog } from './LoadSpoolDialog';
import { RecordUsageDialog } from './RecordUsageDialog';
import { UnloadSpoolDialog } from './UnloadSpoolDialog';

export const STATE_BADGE: Record<
  string,
  { kind: 'green' | 'blue' | 'zinc'; label: string }
> = {
  Using: { kind: 'green', label: 'In use' },
  InStorage: { kind: 'blue', label: 'In storage' },
  Finished: { kind: 'zinc', label: 'Finished' },
};

function remainingIndicatorClass(fraction: number, hasOverrun?: boolean) {
  if (hasOverrun) return 'bg-kind-red';
  if (fraction < 0.15) return 'bg-kind-red';
  if (fraction < 0.4) return 'bg-kind-amber';
  return 'bg-kind-green';
}

export function spoolName(spool: SpoolResponse) {
  return spool.label || spool.colorName || 'spool';
}

export function finishTarget(spool: SpoolResponse): FinishTarget[] {
  return spool.id
    ? [
        {
          id: spool.id,
          name: spoolName(spool),
          remainingGrams: spool.remainingWeightGrams ?? 0,
        },
      ]
    : [];
}

const selectColumn: ColumnDef<SpoolResponse> = {
  id: 'select',
  enableSorting: false,
  enableHiding: false,
  header: ({ table }) => (
    <Checkbox
      aria-label="Select all spools on this page"
      checked={
        table.getIsAllPageRowsSelected() ||
        (table.getIsSomePageRowsSelected() && 'indeterminate')
      }
      onCheckedChange={(checked) =>
        table.toggleAllPageRowsSelected(checked === true)
      }
    />
  ),
  cell: ({ row }) => (
    <Checkbox
      aria-label={`Select ${spoolName(row.original)}`}
      checked={row.getIsSelected()}
      onCheckedChange={(checked) => row.toggleSelected(checked === true)}
    />
  ),
};

interface SpoolsTableProps {
  spools: SpoolResponse[];
  /** Row checkboxes and Finish selected; off for finished spools. */
  selectable?: boolean;
  /** Pages from the server instead of 25 rows a page in the browser, for a list too long to load whole. */
  serverPagination?: ServerTablePagination;
}

export function SpoolsTable({
  spools,
  selectable: selectionRequested = false,
  serverPagination,
}: SpoolsTableProps) {
  const navigate = useNavigate();
  const dialogTriggerRef = useRef<HTMLButtonElement | null>(null);
  const bulkTriggerRef = useRef<HTMLButtonElement>(null);
  const finishReturnRef = useRef<HTMLElement | null>(null);
  const [dialog, setDialog] = useState<{
    mode: 'load' | 'unload' | 'usage';
    spoolId: string;
  } | null>(null);
  const [finishTargets, setFinishTargets] = useState<FinishTarget[]>([]);
  const allowedToFinish = useAllowed('spools:unload');
  const selectable = selectionRequested && allowedToFinish;
  const [selection, setSelection] = useState<RowSelectionState>({});
  const { mutate: restoreSpool } = useRestoreSpool();

  // Selection follows the rows on screen: ids that leave (finished, filtered out) drop out.
  useEffect(() => {
    const ids = new Set(selectable ? spools.map((spool) => spool.id) : []);
    setSelection((previous) => {
      const kept = Object.entries(previous).filter(
        ([id, on]) => on && ids.has(id),
      );
      return kept.length === Object.keys(previous).length
        ? previous
        : Object.fromEntries(kept);
    });
  }, [spools, selectable]);

  const selected = spools.filter((spool) => spool.id && selection[spool.id]);

  const columns = useMemo<ColumnDef<SpoolResponse>[]>(
    () => [
      ...(selectable ? [selectColumn] : []),
      {
        id: 'spool',
        accessorKey: 'colorName',
        meta: { label: 'Spool' },
        header: ({ column }) => (
          <DataTableColumnHeader column={column} title="Spool" />
        ),
        cell: ({ row }) => {
          const spool = row.original;
          return (
            <div className="flex items-center gap-2">
              <Swatch colorHex={spool.colorHex} size="md" />
              <div className="flex flex-col">
                <Link
                  to={`/filament/spools/${spool.id}`}
                  className="font-medium hover:underline"
                >
                  {spool.label || spool.colorName}
                </Link>
                {spool.label && (
                  <span className="text-xs text-muted-foreground">
                    {spool.colorName}
                  </span>
                )}
              </div>
            </div>
          );
        },
      },
      {
        id: 'remaining',
        accessorFn: (spool) =>
          (spool.remainingWeightGrams ?? 0) / (spool.spoolWeightGrams || 1),
        meta: { label: 'Remaining' },
        header: ({ column }) => (
          <DataTableColumnHeader column={column} title="Remaining" />
        ),
        cell: ({ row }) => {
          const spool = row.original;
          const fraction =
            (spool.remainingWeightGrams ?? 0) / (spool.spoolWeightGrams || 1);
          return (
            <div className="flex w-40 flex-col gap-1.5">
              <Progress
                value={Math.min(fraction, 1) * 100}
                indicatorClassName={remainingIndicatorClass(
                  fraction,
                  spool.hasUsageOverrun,
                )}
              />
              <span className="text-xs text-muted-foreground">
                {formatGrams(spool.remainingWeightGrams ?? 0)} /{' '}
                {formatGrams(spool.spoolWeightGrams ?? 0)}
                {spool.hasUsageOverrun ? ' · overrun' : ''}
              </span>
            </div>
          );
        },
      },
      {
        accessorKey: 'state',
        meta: { label: 'State' },
        header: ({ column }) => (
          <DataTableColumnHeader column={column} title="State" />
        ),
        cell: ({ row }) => {
          const meta = STATE_BADGE[row.original.state ?? ''] ?? {
            kind: 'zinc' as const,
            label: row.original.state ?? '',
          };
          return <StatusBadge kind={meta.kind} label={meta.label} />;
        },
      },
      {
        accessorKey: 'feedSlotLabel',
        meta: { label: 'Location' },
        header: ({ column }) => (
          <DataTableColumnHeader column={column} title="Location" />
        ),
        cell: ({ row }) => (
          <span className="font-mono text-xs">
            {row.original.feedSlotLabel ?? '—'}
          </span>
        ),
      },
      {
        id: 'actions',
        enableSorting: false,
        header: () => <span className="sr-only">Actions</span>,
        cell: ({ row }) => {
          const spool = row.original;
          const id = spool.id;
          if (!id) return null;
          return (
            <DataTableRowActions
              permission="spools:load|spools:unload|spools:record-usage"
              label={spoolName(spool)}
              onOpen={(trigger) => {
                dialogTriggerRef.current = trigger;
              }}
            >
              <DropdownMenuGroup>
                {spool.state === 'Finished' ? (
                  <DropdownMenuItem
                    permission="spools:unload"
                    onSelect={() => restoreSpool({ id })}
                  >
                    Restore
                  </DropdownMenuItem>
                ) : (
                  <>
                    {spool.state === 'InStorage' ? (
                      <DropdownMenuItem
                        permission="spools:load"
                        onSelect={() =>
                          setDialog({ mode: 'load', spoolId: id })
                        }
                      >
                        Load
                      </DropdownMenuItem>
                    ) : (
                      <>
                        <DropdownMenuItem
                          permission="spools:record-usage"
                          onSelect={() =>
                            setDialog({ mode: 'usage', spoolId: id })
                          }
                        >
                          Record usage
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          permission="spools:unload"
                          onSelect={() =>
                            setDialog({ mode: 'unload', spoolId: id })
                          }
                        >
                          Unload
                        </DropdownMenuItem>
                      </>
                    )}
                    <DropdownMenuItem
                      permission="spools:unload"
                      onSelect={() => {
                        finishReturnRef.current = dialogTriggerRef.current;
                        setFinishTargets(finishTarget(spool));
                      }}
                    >
                      Finish
                    </DropdownMenuItem>
                  </>
                )}
              </DropdownMenuGroup>
            </DataTableRowActions>
          );
        },
      },
    ],
    [selectable, restoreSpool],
  );

  return (
    <>
      <DataTable
        pageSize={serverPagination ? undefined : 25}
        serverPagination={serverPagination}
        columns={columns}
        data={spools}
        getRowId={(spool, index) => spool.id ?? `missing-${index}`}
        rowSelection={selection}
        onRowSelectionChange={setSelection}
        enableRowSelection={selectable}
        toolbar={
          selectable ? (
            <DataTableSelectionBar
              selectedCount={selected.length}
              label="Spools actions"
              onClear={() => setSelection({})}
            >
              <Button
                permission="spools:unload"
                ref={bulkTriggerRef}
                type="button"
                variant="outline"
                size="sm"
                onClick={() => {
                  finishReturnRef.current = bulkTriggerRef.current;
                  setFinishTargets(selected.flatMap(finishTarget));
                }}
              >
                Finish selected
              </Button>
            </DataTableSelectionBar>
          ) : null
        }
        onRowClick={(spool) => navigate(`/filament/spools/${spool.id}`)}
      />
      <FinishSpoolsDialog
        targets={finishTargets}
        returnFocusRef={finishReturnRef}
        onOpenChange={(open) => {
          if (!open) setFinishTargets([]);
        }}
        onFinished={(ids) =>
          setSelection((previous) =>
            Object.fromEntries(
              Object.entries(previous).filter(([id]) => !ids.includes(id)),
            ),
          )
        }
      />
      {dialog?.mode === 'load' ? (
        <LoadSpoolDialog
          spoolId={dialog.spoolId}
          returnFocusRef={dialogTriggerRef}
          open={dialog !== null}
          onOpenChange={(open) => {
            if (!open) setDialog(null);
          }}
        />
      ) : null}
      {dialog?.mode === 'unload' ? (
        <UnloadSpoolDialog
          spoolId={dialog.spoolId}
          returnFocusRef={dialogTriggerRef}
          open={dialog !== null}
          onOpenChange={(open) => {
            if (!open) setDialog(null);
          }}
        />
      ) : null}
      {dialog?.mode === 'usage' ? (
        <RecordUsageDialog
          spoolId={dialog.spoolId}
          returnFocusRef={dialogTriggerRef}
          open={dialog !== null}
          onOpenChange={(open) => {
            if (!open) setDialog(null);
          }}
        />
      ) : null}
    </>
  );
}
