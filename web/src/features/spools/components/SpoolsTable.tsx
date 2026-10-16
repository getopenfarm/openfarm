import type { ColumnDef } from '@tanstack/react-table';
import { useMemo, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Swatch } from '@/components/swatch/Swatch';
import {
  DropdownMenuGroup,
  DropdownMenuItem,
} from '@/components/ui/dropdown-menu';
import { Progress } from '@/components/ui/progress';
import type { SpoolResponse } from '@/lib/api/generated/endpoints.schemas';
import { formatGrams } from '@/lib/formatUnits';
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

interface SpoolsTableProps {
  spools: SpoolResponse[];
}

export function SpoolsTable({ spools }: SpoolsTableProps) {
  const navigate = useNavigate();
  const dialogTriggerRef = useRef<HTMLButtonElement | null>(null);
  const [dialog, setDialog] = useState<{
    mode: 'load' | 'unload' | 'usage';
    spoolId: string;
  } | null>(null);

  const columns = useMemo<ColumnDef<SpoolResponse>[]>(
    () => [
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
                  to={`/spools/${spool.id}`}
                  className="font-medium hover:underline"
                >
                  {spool.label || spool.colorName}
                </Link>
                <span className="text-xs text-muted-foreground">
                  {spool.material} · {spool.colorName}
                </span>
              </div>
            </div>
          );
        },
      },
      {
        accessorKey: 'vendorName',
        meta: { label: 'Vendor' },
        header: ({ column }) => (
          <DataTableColumnHeader column={column} title="Vendor" />
        ),
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
          if (!id || (spool.state !== 'InStorage' && spool.state !== 'Using'))
            return null;
          return (
            <DataTableRowActions
              label={spool.label || spool.colorName || 'spool'}
              onOpen={(trigger) => {
                dialogTriggerRef.current = trigger;
              }}
            >
              <DropdownMenuGroup>
                {spool.state === 'InStorage' ? (
                  <DropdownMenuItem
                    onSelect={() => setDialog({ mode: 'load', spoolId: id })}
                  >
                    Load
                  </DropdownMenuItem>
                ) : (
                  <>
                    <DropdownMenuItem
                      onSelect={() => setDialog({ mode: 'usage', spoolId: id })}
                    >
                      Record usage
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      onSelect={() =>
                        setDialog({ mode: 'unload', spoolId: id })
                      }
                    >
                      Unload
                    </DropdownMenuItem>
                  </>
                )}
              </DropdownMenuGroup>
            </DataTableRowActions>
          );
        },
      },
    ],
    [],
  );

  return (
    <>
      <DataTable
        pageSize={25}
        columns={columns}
        data={spools}
        toolbar={null}
        onRowClick={(spool) => navigate(`/spools/${spool.id}`)}
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
