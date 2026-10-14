import { PlusIcon } from 'lucide-react';
import { useRef, useState } from 'react';
import { useNavigate } from 'react-router';
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
import type { ListPartsResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPartsListPartsEndpoint } from '@/lib/api/generated/parts/parts';
import { PartFilterBar } from './components/PartFilterBar';
import { PartsTable } from './components/PartsTable';
import { usePartsHub } from './hooks/usePartsHub';

export function PartsPage() {
  const addPartRef = useRef<HTMLButtonElement>(null);
  const navigate = useNavigate();
  usePartsHub();

  const [removalPending, setRemovalPending] = useState(false);
  const list = useServerListState('part', 'name');
  const { page, pageSize, setPage, setPageSize } = list;
  const [selectedTags, setSelectedTags] = useState<string[]>([]);

  const partsQuery = useOpenFarmApiEndpointsPartsListPartsEndpoint(
    {
      tag: selectedTags.length > 0 ? selectedTags : undefined,
      ...list.params,
    },
    { query: serverListQueryOptions },
  );

  const listData = useRetainedListData(
    partsQuery.data?.data as ListPartsResponse | undefined,
  );
  const parts = listData?.parts ?? [];
  useServerPageRecovery(
    page,
    pageSize,
    listData?.totalCount ?? 0,
    partsQuery.isSuccess &&
      !partsQuery.isFetching &&
      !partsQuery.isPlaceholderData,
    setPage,
  );

  const totalCount = listData?.totalCount ?? 0;
  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize));

  function toggleTag(tag: string) {
    setPage(1);
    setSelectedTags((prev) =>
      prev.includes(tag) ? prev.filter((t) => t !== tag) : [...prev, tag],
    );
  }

  return (
    <div className="flex flex-col gap-6 has-[[data-slot=table-selection-bar]]:pb-[calc(8rem+env(safe-area-inset-bottom))]">
      <PageHeader
        title="Part library"
        description={
          listData ? `${listData.totalCount ?? parts.length} parts` : undefined
        }
        action={
          <Button ref={addPartRef} onClick={() => navigate('/parts/new')}>
            <PlusIcon data-icon="inline-start" /> Add part
          </Button>
        }
      />

      <div className="flex min-w-0 flex-col gap-4">
        <fieldset
          className="shrink-0"
          disabled={removalPending}
          aria-label="Filter parts"
        >
          <PartFilterBar selectedTags={selectedTags} onToggle={toggleTag} />
        </fieldset>

        {partsQuery.isError && listData ? (
          <ErrorState
            title="Could not refresh this list"
            description="Previous results are shown. Please try again."
            onRetry={() => partsQuery.refetch()}
          />
        ) : null}
        {partsQuery.isLoading ? (
          <Skeleton className="h-64 w-full" />
        ) : partsQuery.isError && !listData ? (
          <ErrorState
            title="Could not load parts"
            description="Please try again."
            onRetry={() => partsQuery.refetch()}
          />
        ) : page > pageCount && !partsQuery.isPlaceholderData ? (
          <Skeleton className="h-64 w-full" />
        ) : parts.length === 0 && selectedTags.length > 0 ? (
          <EmptyState
            title="No parts match"
            description="Try a different tag filter."
          />
        ) : parts.length === 0 ? (
          <EmptyState
            title="No parts yet"
            description="Add an STL, 3MF, or G-code file to start your catalog."
            action={
              <Button
                disabled={removalPending}
                onClick={() => navigate('/parts/new')}
              >
                <PlusIcon data-icon="inline-start" /> Add part
              </Button>
            }
          />
        ) : (
          <PartsTable
            parts={parts}
            selectionKey={JSON.stringify([
              page,
              pageSize,
              selectedTags,
              list.sorting,
            ])}
            sorting={{
              ...list.serverSorting,
              disabled: partsQuery.isError,
              pending: partsQuery.isFetching || removalPending,
            }}
            pagination={{
              page,
              pageSize,
              totalCount,
              onPageChange: setPage,
              onPageSizeChange: (size) => {
                setPageSize(size);
                setPage(1);
              },
              disabled: partsQuery.isError,
              pending:
                partsQuery.isFetching ||
                partsQuery.isPlaceholderData ||
                removalPending,
              label: 'Parts',
            }}
            onPendingChange={setRemovalPending}
            fallbackFocusRef={addPartRef}
            onRowClick={(part) => navigate(`/parts/${part.id}`)}
          />
        )}
      </div>
    </div>
  );
}
