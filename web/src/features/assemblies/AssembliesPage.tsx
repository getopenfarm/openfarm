import type { ColumnDef } from '@tanstack/react-table';
import { Layers3Icon, PlusIcon, PrinterIcon } from 'lucide-react';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { Button } from '@/components/ui/button';
import { formatMinutes } from '@/lib/formatMinutes';
import { type Assembly, useAssemblies } from './data/mockAssembliesStore';
import { computeAssemblyTotals } from './lib/computeAssemblyTotals';

export function AssembliesPage() {
  const navigate = useNavigate();
  const [assemblies] = useAssemblies();

  // Intentionally minimal — unlike Parts' print flow, "Print" here doesn't
  // open a queue modal or write to the Jobs mock store. It just queues (in
  // spirit) and hands off to the Jobs page.
  function handlePrint(assembly: Assembly) {
    toast.success(`Queued ${assembly.name} — check the print queue.`);
    navigate('/jobs');
  }

  const columns: ColumnDef<Assembly>[] = [
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
            <span className="font-medium">{row.original.name}</span>
            <span className="text-xs text-muted-foreground">
              {row.original.description}
            </span>
          </div>
        </div>
      ),
    },
    {
      id: 'components',
      header: 'Components',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.components.length} components
        </span>
      ),
    },
    {
      id: 'totalParts',
      header: 'Total parts',
      cell: ({ row }) => {
        const totals = computeAssemblyTotals(row.original, assemblies);
        return <span className="font-mono text-sm">{totals.totalParts}</span>;
      },
    },
    {
      id: 'estimatedTime',
      header: 'Est. time',
      cell: ({ row }) => {
        const totals = computeAssemblyTotals(row.original, assemblies);
        return (
          <span className="font-mono text-sm">
            {formatMinutes(totals.estimatedTimeMinutes)}
          </span>
        );
      },
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      cell: ({ row }) => (
        <div className="flex justify-end">
          <Button
            variant="outline"
            size="sm"
            onClick={(event) => {
              event.stopPropagation();
              handlePrint(row.original);
            }}
          >
            <PrinterIcon /> Print
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h2 className="font-heading text-2xl font-semibold">Assemblies</h2>
          <p className="text-sm text-muted-foreground">
            Group parts (and other assemblies) into reusable bills of materials.
          </p>
        </div>
        <Button onClick={() => navigate('/assemblies/new')}>
          <PlusIcon /> New assembly
        </Button>
      </div>

      <DataTable
        columns={columns}
        data={assemblies}
        onRowClick={(row) => navigate(`/assemblies/${row.id}/edit`)}
        emptyState={
          <div className="flex flex-col items-center gap-1 py-4">
            <p className="text-sm font-medium">No assemblies yet</p>
            <p className="text-sm text-muted-foreground">
              Create one to group parts into a bill of materials.
            </p>
          </div>
        }
      />
    </div>
  );
}
