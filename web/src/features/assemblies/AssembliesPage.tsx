import type { ColumnDef } from '@tanstack/react-table';
import { Layers3Icon, PlusIcon } from 'lucide-react';
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
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useOpenFarmApiEndpointsAssembliesListAssembliesEndpoint } from '@/lib/api/generated/assemblies/assemblies';
import type {
  AssemblySummaryResponse,
  ListAssembliesResponse,
} from '@/lib/api/generated/endpoints.schemas';

const columns: ColumnDef<AssemblySummaryResponse>[] = [
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
          <Link
            to={`/assemblies/${row.original.id}`}
            className="font-medium hover:underline"
          >
            {row.original.name}
          </Link>
          {row.original.description && (
            <span className="text-xs text-muted-foreground">
              {row.original.description}
            </span>
          )}
        </div>
      </div>
    ),
  },
  {
    id: 'lineCount',
    header: 'Lines',
    cell: ({ row }) => (
      <span className="text-sm text-muted-foreground">
        {row.original.lineCount} lines
      </span>
    ),
  },
];

export function AssembliesPage() {
  const navigate = useNavigate();
  const list = useServerListState('assembly', 'name');
  const { page, pageSize, setPage, setPageSize } = list;

  const assembliesQuery =
    useOpenFarmApiEndpointsAssembliesListAssembliesEndpoint(
      {
        ...list.params,
      },
      { query: serverListQueryOptions },
    );
  const listData = useRetainedListData(
    assembliesQuery.data?.data as ListAssembliesResponse | undefined,
  );
  const assemblies = listData?.items ?? [];
  useServerPageRecovery(
    page,
    pageSize,
    listData?.totalCount ?? 0,
    assembliesQuery.isSuccess &&
      !assembliesQuery.isFetching &&
      !assembliesQuery.isPlaceholderData,
    setPage,
  );

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Assemblies"
        description="Group parts (and other assemblies) into reusable bills of materials."
        action={
          <Button onClick={() => navigate('/assemblies/new')}>
            <PlusIcon data-icon="inline-start" /> Add assembly
          </Button>
        }
      />

      {assembliesQuery.isError && listData ? (
        <ErrorState
          title="Could not refresh this list"
          description="Previous results are shown. Please try again."
          onRetry={() => assembliesQuery.refetch()}
        />
      ) : null}
      {assembliesQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : assembliesQuery.isError && !listData ? (
        <ErrorState
          title="Could not load assemblies"
          onRetry={() => assembliesQuery.refetch()}
        />
      ) : assemblies.length === 0 && page === 1 ? (
        <EmptyState
          icon={<Layers3Icon className="size-8 text-muted-foreground" />}
          title="No assemblies yet"
          description="Create one to group parts into a bill of materials."
          action={
            <Button onClick={() => navigate('/assemblies/new')}>
              <PlusIcon data-icon="inline-start" /> Add assembly
            </Button>
          }
        />
      ) : (
        <DataTable
          serverSorting={{
            ...list.serverSorting,
            disabled: assembliesQuery.isError,
            pending: assembliesQuery.isFetching,
          }}
          serverPagination={{
            page,
            pageSize,
            totalCount: listData?.totalCount ?? assemblies.length,
            onPageChange: setPage,
            onPageSizeChange: (size) => {
              setPageSize(size);
              setPage(1);
            },
            disabled: assembliesQuery.isError,
            pending:
              assembliesQuery.isFetching || assembliesQuery.isPlaceholderData,
          }}
          columns={columns}
          data={assemblies}
          onRowClick={(row) => navigate(`/assemblies/${row.id}`)}
        />
      )}
    </div>
  );
}
