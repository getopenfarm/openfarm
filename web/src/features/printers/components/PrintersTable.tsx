import type { ColumnDef } from '@tanstack/react-table';
import {
  CheckCircle2Icon,
  MoreHorizontalIcon,
  PencilIcon,
  Trash2Icon,
} from 'lucide-react';
import { DataTable } from '@/components/data-table/DataTable';
import type { PrinterStatus } from '@/components/printer-tile/status';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Progress } from '@/components/ui/progress';
import type { MatchedMaintenanceFlag } from '@/features/maintenance/lib/matchMaintenanceToFleet';
import type { FleetPrinterResponse } from '@/lib/api/generated/endpoints.schemas';
import { printerStatusToBadge } from '../lib/printerStatusBadge';

const PLACEHOLDER_FILAMENT_COLOR = '#8892a6';

interface PrintersTableProps {
  printers: FleetPrinterResponse[];
  maintenanceByPrinterId: Map<string, MatchedMaintenanceFlag>;
  onOpen: (id: string) => void;
  onMarkReady: (id: string) => void;
  onEdit: (id: string) => void;
  onRemove: (id: string, name: string) => void;
}

export function PrintersTable({
  printers,
  maintenanceByPrinterId,
  onOpen,
  onMarkReady,
  onEdit,
  onRemove,
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
        return (
          <div className="flex justify-end">
            {printer.status === 'AwaitingClear' ? (
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
            ) : (
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`Actions for ${printer.name}`}
                    onClick={(event) => event.stopPropagation()}
                  >
                    <MoreHorizontalIcon />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent
                  align="end"
                  onClick={(event) => event.stopPropagation()}
                >
                  <DropdownMenuItem onSelect={() => onEdit(printer.id ?? '')}>
                    <PencilIcon /> Edit
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    variant="destructive"
                    onSelect={() =>
                      onRemove(printer.id ?? '', printer.name ?? '')
                    }
                  >
                    <Trash2Icon /> Remove
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            )}
          </div>
        );
      },
    },
  ];

  return (
    <DataTable
      columns={columns}
      data={printers}
      onRowClick={(row) => onOpen(row.id ?? '')}
      emptyState="No printers match."
    />
  );
}
