import type { ColumnDef } from '@tanstack/react-table';
import { ClipboardListIcon, PlusIcon } from 'lucide-react';
import { Link, useNavigate } from 'react-router';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import {
  serverListQueryOptions,
  useRetainedListData,
  useServerListState,
  useServerPageRecovery,
} from '@/components/data-table/useServerListState';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
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
          <Link
            to={`/production-runs/${row.original.id}`}
            className="font-medium hover:underline"
          >
            {row.original.displayLabel}
          </Link>
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

export function ProductionRunsPage() {
  const navigate = useNavigate();
  const list = useServerListState('run', 'displayLabel');
  const { page, pageSize, setPage, setPageSize } = list;
  useProductionHub();

  const runsQuery =
    useOpenFarmApiEndpointsProductionRunsListProductionRunsEndpoint(
      {
        ...list.params,
      },
      { query: serverListQueryOptions },
    );
  const listData = useRetainedListData(
    runsQuery.data?.data as ListProductionRunsResponse | undefined,
  );
  const runs = listData?.items ?? [];
  useServerPageRecovery(
    page,
    pageSize,
    listData?.totalCount ?? 0,
    runsQuery.isSuccess &&
      !runsQuery.isFetching &&
      !runsQuery.isPlaceholderData,
    setPage,
  );

  return (
    <Page>
      <PageHeader
        title="Production runs"
        description="Group parts/assemblies into a run and track their combined progress."
        action={
          <Button onClick={() => navigate('/production-runs/new')}>
            <PlusIcon data-icon="inline-start" /> Add run
          </Button>
        }
      />

      {runsQuery.isError && listData ? (
        <ErrorState
          title="Could not refresh list"
          description="Showing previous results."
          onRetry={() => runsQuery.refetch()}
        />
      ) : null}
      {runsQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : runsQuery.isError && !listData ? (
        <ErrorState
          title="Could not load production runs"
          onRetry={() => runsQuery.refetch()}
        />
      ) : runs.length === 0 && page === 1 ? (
        <EmptyState
          icon={<ClipboardListIcon className="size-8 text-muted-foreground" />}
          title="No production runs"
          description="Add one to queue parts or assemblies for printing."
          action={
            <Button onClick={() => navigate('/production-runs/new')}>
              <PlusIcon data-icon="inline-start" /> Add run
            </Button>
          }
        />
      ) : (
        <DataTable
          serverSorting={{
            ...list.serverSorting,
            disabled: runsQuery.isError,
            pending: runsQuery.isFetching,
          }}
          serverPagination={{
            page,
            pageSize,
            totalCount: listData?.totalCount ?? runs.length,
            onPageChange: setPage,
            onPageSizeChange: (size) => {
              setPageSize(size);
              setPage(1);
            },
            disabled: runsQuery.isError,
            pending: runsQuery.isFetching || runsQuery.isPlaceholderData,
          }}
          columns={columns}
          data={runs}
          onRowClick={(row) => navigate(`/production-runs/${row.id}`)}
        />
      )}
    </Page>
  );
}
