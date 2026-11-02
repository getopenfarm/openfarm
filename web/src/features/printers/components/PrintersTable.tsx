import type { ColumnDef } from '@tanstack/react-table';
import { Link } from 'react-router';
import { DataTable } from '@/components/data-table/DataTable';
import type { PrinterStatus } from '@/components/printer-tile/status';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Swatch } from '@/components/swatch/Swatch';
import { Progress } from '@/components/ui/progress';
import type { FleetPrinterResponse } from '@/lib/api/generated/endpoints.schemas';
import { useAllowed } from '@/lib/auth/AccessContext';
import type { DensityMode } from '@/lib/preferences/types';
import { usePrinterSendProgress } from '../hooks/usePrinterSendProgress';
import { printerStatusToBadge } from '../lib/printerStatusBadge';

function PrinterStatusCell({ printer }: { printer: FleetPrinterResponse }) {
  const { kind, label } = printerStatusToBadge(
    (printer.status ?? 'Idle') as PrinterStatus,
    printer.isReachable ?? false,
    usePrinterSendProgress(printer.id ?? ''),
  );
  return <StatusBadge kind={kind} label={label} />;
}

const PLACEHOLDER_FILAMENT_COLOR = '#8892a6';

// Row vertical padding per density — Small tightens rows so more printers fit per screen.
const DENSITY_CELL_PADDING: Record<DensityMode, string> = {
  small: 'py-1',
  medium: 'py-2',
  large: 'py-4',
};

/** Due-soon and overdue maintenance per printer. */
export interface MaintenanceCounts {
  overdue: number;
  dueSoon: number;
}

interface PrintersTableProps {
  printers: FleetPrinterResponse[];
  maintenanceByPrinterId: Map<string, MaintenanceCounts>;
  density?: DensityMode;
  onOpen: (id: string) => void;
}

export function PrintersTable({
  printers,
  maintenanceByPrinterId,
  density = 'medium',
  onOpen,
}: PrintersTableProps) {
  const showMaintenance = useAllowed('maintenance:view');
  const columns: ColumnDef<FleetPrinterResponse>[] = [
    {
      id: 'printer',
      header: 'Printer',
      accessorFn: (row) => row.name ?? '',
      cell: ({ row }) => (
        <div className="flex flex-col gap-0.5">
          <Link
            className="font-medium hover:underline"
            to={`/printers/${row.original.id}`}
          >
            {row.original.name}
          </Link>
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
      cell: ({ row }) => <PrinterStatusCell printer={row.original} />,
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
    ...(showMaintenance
      ? [
          {
            id: 'maintenance',
            header: 'Maintenance',
            cell: ({ row }) => {
              const counts = maintenanceByPrinterId.get(row.original.id ?? '');
              if (!counts)
                return <span className="text-sm text-muted-foreground">—</span>;
              return (
                <div className="flex flex-wrap gap-1">
                  {counts.overdue > 0 && (
                    <StatusBadge
                      kind="red"
                      label={`${counts.overdue} overdue`}
                    />
                  )}
                  {counts.dueSoon > 0 && (
                    <StatusBadge
                      kind="amber"
                      label={`${counts.dueSoon} due soon`}
                    />
                  )}
                </div>
              );
            },
          } satisfies ColumnDef<FleetPrinterResponse>,
        ]
      : []),
  ];

  return (
    <DataTable
      pageSize={25}
      columns={columns}
      data={printers}
      cellClassName={DENSITY_CELL_PADDING[density]}
      onRowClick={(row) => onOpen(row.id ?? '')}
    />
  );
}
