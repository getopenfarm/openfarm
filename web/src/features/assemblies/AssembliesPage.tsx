import { Layers3Icon, PlusIcon } from 'lucide-react';
import { useRef } from 'react';
import { useNavigate } from 'react-router';
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
import { useOpenFarmApiEndpointsAssembliesListAssembliesEndpoint } from '@/lib/api/generated/assemblies/assemblies';
import type { ListAssembliesResponse } from '@/lib/api/generated/endpoints.schemas';
import { AssembliesTable } from './components/AssembliesTable';

export function AssembliesPage() {
  const addAssemblyRef = useRef<HTMLButtonElement>(null);
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
    <Page>
      <PageHeader
        title="Assemblies"
        description="Group parts (and other assemblies) into reusable bills of materials."
        action={
          <Button
            ref={addAssemblyRef}
            onClick={() => navigate('/assemblies/new')}
          >
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
        <AssembliesTable
          assemblies={assemblies}
          selectionKey={JSON.stringify([page, pageSize, list.sorting])}
          fallbackFocusRef={addAssemblyRef}
          sorting={{
            ...list.serverSorting,
            disabled: assembliesQuery.isError,
            pending: assembliesQuery.isFetching,
          }}
          pagination={{
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
          onRowClick={(row) => navigate(`/assemblies/${row.id}`)}
        />
      )}
    </Page>
  );
}
