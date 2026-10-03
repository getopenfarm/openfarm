import type { ColumnDef } from '@tanstack/react-table';
import { ClipboardListIcon, PlusIcon } from 'lucide-react';
import { useNavigate } from 'react-router';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import type {
  ListProductionRunsResponse,
  ProductionRunSummaryResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsProductionRunsListProductionRunsEndpoint } from '@/lib/api/generated/production-runs/production-runs';
import { useProductionHub } from './hooks/useProductionHub';

const PAGE_SIZE = 100;

export function ProductionRunsPage() {
  const navigate = useNavigate();
  useProductionHub();

  const runsQuery =
    useOpenFarmApiEndpointsProductionRunsListProductionRunsEndpoint({
      page: 1,
      pageSize: PAGE_SIZE,
    });
  const listData = runsQuery.data?.data as
    | ListProductionRunsResponse
    | undefined;
  const runs = listData?.items ?? [];

  const columns: ColumnDef<ProductionRunSummaryResponse>[] = [
    {
      id: 'run',
      accessorKey: 'displayLabel',
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Run" />
      ),
      cell: ({ row }) => (
        <div className="flex items-center gap-3">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
            <ClipboardListIcon className="size-5" />
          </div>
          <div className="flex flex-col">
            <span className="font-medium">{row.original.displayLabel}</span>
            {row.original.dueDate && (
              <span className="text-xs text-muted-foreground">
                Due {row.original.dueDate}
              </span>
            )}
          </div>
        </div>
      ),
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => <Badge variant="outline">{row.original.status}</Badge>,
    },
    {
      id: 'lineCount',
      header: 'Lines',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.lineCount}
        </span>
      ),
    },
    {
      id: 'jobSummary',
      header: 'Jobs',
      cell: ({ row }) => {
        const summary = row.original.jobSummary;
        const failedOrCancelled =
          (summary?.failed ?? 0) + (summary?.cancelled ?? 0);
        return (
          <span className="text-sm text-muted-foreground">
            {summary?.queued ?? 0} queued · {summary?.printing ?? 0} printing ·{' '}
            {summary?.completed ?? 0} completed
            {failedOrCancelled > 0
              ? ` · ${failedOrCancelled} failed/cancelled`
              : ''}
          </span>
        );
      },
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Production runs"
        description="Group parts/assemblies into a run and track their combined progress."
        action={
          <Button onClick={() => navigate('/production-runs/new')}>
            <PlusIcon /> New run
          </Button>
        }
      />

      {runsQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : runs.length === 0 ? (
        <EmptyState
          icon={<ClipboardListIcon className="size-8 text-muted-foreground" />}
          title="No production runs yet"
          description="Create one to queue parts/assemblies for printing."
          action={
            <Button onClick={() => navigate('/production-runs/new')}>
              <PlusIcon /> New run
            </Button>
          }
        />
      ) : (
        <DataTable
          columns={columns}
          data={runs}
          onRowClick={(row) => navigate(`/production-runs/${row.id}`)}
        />
      )}
    </div>
  );
}
