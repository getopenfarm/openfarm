import type { ColumnDef } from '@tanstack/react-table';
import { useState } from 'react';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import { Progress } from '@/components/ui/progress';
import type { SpoolResponse } from '@/lib/api/generated/endpoints.schemas';
import { LoadSpoolDialog } from './LoadSpoolDialog';
import { RecordUsageDialog } from './RecordUsageDialog';
import { UnloadSpoolDialog } from './UnloadSpoolDialog';

const STATE_BADGE: Record<
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
  const [dialog, setDialog] = useState<{
    mode: 'load' | 'unload' | 'usage';
    spoolId: string;
  } | null>(null);

  const columns: ColumnDef<SpoolResponse>[] = [
    {
      id: 'spool',
      accessorKey: 'colorName',
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Spool" />
      ),
      cell: ({ row }) => {
        const spool = row.original;
        return (
          <div className="flex items-center gap-2">
            <Swatch colorHex={spool.colorHex} size="md" />
            <div className="flex flex-col">
              <span className="font-medium">
                {spool.label || spool.colorName}
              </span>
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
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Vendor" />
      ),
    },
    {
      id: 'remaining',
      accessorFn: (spool) =>
        (spool.remainingWeightGrams ?? 0) / (spool.spoolWeightGrams || 1),
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
              {spool.remainingWeightGrams} / {spool.spoolWeightGrams} g
              {spool.hasUsageOverrun ? ' · overrun' : ''}
            </span>
          </div>
        );
      },
    },
    {
      accessorKey: 'state',
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
      header: () => <span className="sr-only">Actions</span>,
      cell: ({ row }) => {
        const spool = row.original;
        if (!spool.id) return null;
        if (spool.state === 'InStorage') {
          return (
            <div className="flex justify-end">
              <Button
                variant="outline"
                size="sm"
                onClick={(event) => {
                  event.stopPropagation();
                  setDialog({ mode: 'load', spoolId: spool.id ?? '' });
                }}
              >
                Load
              </Button>
            </div>
          );
        }
        if (spool.state === 'Using') {
          return (
            <div className="flex justify-end gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={(event) => {
                  event.stopPropagation();
                  setDialog({ mode: 'usage', spoolId: spool.id ?? '' });
                }}
              >
                Record usage
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={(event) => {
                  event.stopPropagation();
                  setDialog({ mode: 'unload', spoolId: spool.id ?? '' });
                }}
              >
                Remove
              </Button>
            </div>
          );
        }
        return null;
      },
    },
  ];

  return (
    <>
      <DataTable columns={columns} data={spools} />
      {dialog?.mode === 'load' ? (
        <LoadSpoolDialog
          spoolId={dialog.spoolId}
          open={dialog !== null}
          onOpenChange={(open) => {
            if (!open) setDialog(null);
          }}
        />
      ) : null}
      {dialog?.mode === 'unload' ? (
        <UnloadSpoolDialog
          spoolId={dialog.spoolId}
          open={dialog !== null}
          onOpenChange={(open) => {
            if (!open) setDialog(null);
          }}
        />
      ) : null}
      {dialog?.mode === 'usage' ? (
        <RecordUsageDialog
          spoolId={dialog.spoolId}
          open={dialog !== null}
          onOpenChange={(open) => {
            if (!open) setDialog(null);
          }}
        />
      ) : null}
    </>
  );
}
