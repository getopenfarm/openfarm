import type { ColumnDef } from '@tanstack/react-table';
import { CheckCircle2Icon } from 'lucide-react';
import { DataTable } from '@/components/data-table/DataTable';
import type { PrinterStatus } from '@/components/printer-tile/status';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import { Progress } from '@/components/ui/progress';
import type { MatchedMaintenanceFlag } from '@/features/maintenance/lib/matchMaintenanceToFleet';
import type { FleetPrinterResponse } from '@/lib/api/generated/endpoints.schemas';
import type { DensityMode } from '@/lib/preferences/types';
import { printerStatusToBadge } from '../lib/printerStatusBadge';

const PLACEHOLDER_FILAMENT_COLOR = '#8892a6';

// Row vertical padding per density — Small tightens rows so more printers fit per screen.
const DENSITY_CELL_PADDING: Record<DensityMode, string> = {
  small: 'py-1',
  medium: 'py-2',
  large: 'py-4',
};

interface PrintersTableProps {
  printers: FleetPrinterResponse[];
  maintenanceByPrinterId: Map<string, MatchedMaintenanceFlag>;
  density?: DensityMode;
  onOpen: (id: string) => void;
  onMarkReady: (id: string) => void;
}

export function PrintersTable({
  printers,
  maintenanceByPrinterId,
  density = 'medium',
  onOpen,
  onMarkReady,
}: PrintersTableProps) {
  const columns: ColumnDef<FleetPrinterResponse>[] = [
    {
      id: 'printer',
      header: 'Printer',
      accessorFn: (row) => row.name ?? '',
      cell: ({ row }) => (
        <div className="flex flex-col gap-0.5">
          <span className="font-medium">{row.original.name}</span>
          <span className="font-mono text-xs text-muted-foreground">
            {row.original.brand} {row.original.modelCode}
          </span>
        </div>
      ),
    },
    {
      id: 'status',
      header: 'Status',
      accessorFn: (row) => row.status ?? '',
      cell: ({ row }) => {
        const { kind, label } = printerStatusToBadge(
          (row.original.status ?? 'Idle') as PrinterStatus,
          row.original.isReachable ?? false,
        );
        return <StatusBadge kind={kind} label={label} />;
      },
    },
    {
      id: 'job',
      header: 'Current job',
      cell: ({ row }) => {
        const printer = row.original;
        if (printer.status === 'Printing') {
          const progress = printer.printProgressPercent ?? 0;
          return (
            <div className="flex min-w-40 flex-col gap-1.5">
              <Progress
                value={progress}
                className="h-1.5"
                indicatorClassName="bg-status-printing"
              />
              <span className="font-mono text-xs text-muted-foreground">
                {progress}%
              </span>
            </div>
          );
        }
        return (
          <span className="text-sm text-muted-foreground">No active job</span>
        );
      },
    },
    {
      id: 'filament',
      header: 'Filament',
      cell: () => <Swatch colorHex={PLACEHOLDER_FILAMENT_COLOR} size="sm" />,
    },
    {
      id: 'maintenance',
      header: 'Maintenance',
      cell: ({ row }) => {
        const flag = maintenanceByPrinterId.get(row.original.id ?? '');
        if (!flag) {
          return <span className="text-sm text-muted-foreground">—</span>;
        }
        return (
          <StatusBadge
            kind={flag.severity === 'overdue' ? 'red' : 'amber'}
            label={flag.severity === 'overdue' ? 'Overdue' : 'Due soon'}
          />
        );
      },
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      cell: ({ row }) => {
        const printer = row.original;
        if (printer.status !== 'AwaitingClear') {
          return null;
        }
        return (
          <div className="flex justify-end">
            <Button
              variant="outline"
              size="sm"
              onClick={(event) => {
                event.stopPropagation();
                onMarkReady(printer.id ?? '');
              }}
            >
              <CheckCircle2Icon /> Clear
            </Button>
          </div>
        );
      },
    },
  ];

  return (
    <DataTable
      columns={columns}
      data={printers}
      cellClassName={DENSITY_CELL_PADDING[density]}
      onRowClick={(row) => onOpen(row.id ?? '')}
      emptyState="No printers match."
    />
  );
}
