import type { ColumnDef } from '@tanstack/react-table';
import { PackageIcon, PlusIcon } from 'lucide-react';
import { useMemo } from 'react';
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
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useCurrency } from '@/features/settings/generalSettings';
import type {
  ListNonPrintedComponentsResponse,
  NonPrintedComponentResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpoint } from '@/lib/api/generated/non-printed-components/non-printed-components';
import { formatCost } from '@/lib/formatUnits';

const makeColumns = (
  currency: string,
): ColumnDef<NonPrintedComponentResponse>[] => [
  {
    id: 'name',
    accessorKey: 'name',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Name" />
    ),
    cell: ({ row }) => (
      <div className="flex items-center gap-3">
        <div className="flex size-10 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
          <PackageIcon className="size-5" />
        </div>
        <Link
          to={`/non-printed-components/${row.original.id}/edit`}
          className="font-medium hover:underline"
        >
          {row.original.name}
        </Link>
      </div>
    ),
  },
  {
    id: 'unitCost',
    header: 'Cost per piece',
    cell: ({ row }) => (
      <span className="font-mono text-sm">
        {row.original.unitCost != null
          ? formatCost(row.original.unitCost, currency)
          : '—'}
      </span>
    ),
  },
];

export function NonPrintedComponentsPage() {
  const currency = useCurrency();
  const columns = useMemo(() => makeColumns(currency), [currency]);
  const navigate = useNavigate();
  const list = useServerListState('name', 'name');
  const { page, pageSize, setPage, setPageSize } = list;

  const componentsQuery =
    useOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpoint(
      list.params,
      { query: serverListQueryOptions },
    );
  const listData = useRetainedListData(
    componentsQuery.data?.data as ListNonPrintedComponentsResponse | undefined,
  );
  const components = listData?.items ?? [];
  useServerPageRecovery(
    page,
    pageSize,
    listData?.totalCount ?? 0,
    componentsQuery.isSuccess &&
      !componentsQuery.isFetching &&
      !componentsQuery.isPlaceholderData,
    setPage,
  );

  return (
    <Page>
      <PageHeader
        title="Non-printed components"
        description="Screws, inserts, packaging, and other reusable BOM components that are not printed."
        action={
          <Button onClick={() => navigate('/non-printed-components/new')}>
            <PlusIcon data-icon="inline-start" /> Add component
          </Button>
        }
      />

      {componentsQuery.isError && listData ? (
        <ErrorState
          title="Could not refresh list"
          description="Showing previous results."
          onRetry={() => componentsQuery.refetch()}
        />
      ) : null}
      {componentsQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : componentsQuery.isError && !listData ? (
        <ErrorState
          title="Could not load components"
          onRetry={() => componentsQuery.refetch()}
        />
      ) : components.length === 0 && page === 1 ? (
        <EmptyState
          icon={<PackageIcon className="size-8 text-muted-foreground" />}
          title="No non-printed components"
          description="Add one to reference it from a BOM line."
          action={
            <Button onClick={() => navigate('/non-printed-components/new')}>
              <PlusIcon data-icon="inline-start" /> Add component
            </Button>
          }
        />
      ) : (
        <DataTable
          serverSorting={{
            ...list.serverSorting,
            disabled: componentsQuery.isError,
            pending: componentsQuery.isFetching,
          }}
          serverPagination={{
            page,
            pageSize,
            totalCount: listData?.totalCount ?? components.length,
            onPageChange: setPage,
            onPageSizeChange: (size) => {
              setPageSize(size);
              setPage(1);
            },
            disabled: componentsQuery.isError,
            pending:
              componentsQuery.isFetching || componentsQuery.isPlaceholderData,
          }}
          columns={columns}
          data={components}
          onRowClick={(row) =>
            navigate(`/non-printed-components/${row.id}/edit`)
          }
        />
      )}
    </Page>
  );
}
