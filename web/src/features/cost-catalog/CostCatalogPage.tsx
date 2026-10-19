import { useQuery } from '@tanstack/react-query';
import { PackageIcon, PlusIcon } from 'lucide-react';
import { useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { DataTableToolbar } from '@/components/data-table/DataTableToolbar';
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
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import type { ListCostItemsResponse } from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
import { pluralize } from '@/lib/pluralize';
import { catalogKey } from './api';
import { CostItemsTable } from './CostItemsTable';
import { categories, categoryLabel } from './costing';

export function CostCatalogPage() {
  const navigate = useNavigate();
  const addRef = useRef<HTMLButtonElement>(null);
  const list = useServerListState('name', 'name');
  const { page, pageSize, setPage, setPageSize } = list;
  const [itemType, setItemType] = useState('all');
  const [category, setCategory] = useState('all');
  const [removalPending, setRemovalPending] = useState(false);
  const filtered = itemType !== 'all' || category !== 'all';
  const params = {
    ...list.params,
    ...(itemType === 'all' ? {} : { itemType }),
    ...(category === 'all' ? {} : { category }),
  };
  const query = useQuery({
    queryKey: [catalogKey, params],
    queryFn: ({ signal }) =>
      httpClient<{ data: ListCostItemsResponse }>(
        `${catalogKey}?${new URLSearchParams(Object.entries(params).map(([key, value]) => [key, String(value)]))}`,
        { signal },
      ),
    ...serverListQueryOptions,
  });
  const data = useRetainedListData(query.data?.data);
  const items = data?.items ?? [];
  const pending = query.isFetching || removalPending;
  useServerPageRecovery(
    page,
    pageSize,
    data?.totalCount ?? 0,
    query.isSuccess && !query.isFetching && !query.isPlaceholderData,
    setPage,
  );
  function clearFilters() {
    setItemType('all');
    setCategory('all');
    setPage(1);
  }

  return (
    <Page className="pb-[calc(10rem+env(safe-area-inset-bottom))]">
      <PageHeader
        title="Cost catalog"
        description={
          data
            ? pluralize(data.totalCount ?? items.length, 'cost item')
            : undefined
        }
        action={
          <Button
            ref={addRef}
            disabled={removalPending}
            onClick={() => navigate('/cost-catalog/new')}
          >
            <PlusIcon data-icon="inline-start" />
            Add cost item
          </Button>
        }
      />
      <div className="flex min-w-0 flex-col gap-4">
        <DataTableToolbar>
          <ToggleGroup
            type="single"
            variant="outline"
            size="sm"
            value={itemType}
            aria-label="Filter by type"
            disabled={pending}
            onValueChange={(value) => {
              if (value) {
                setItemType(value);
                setPage(1);
              }
            }}
          >
            <ToggleGroupItem value="all">All items</ToggleGroupItem>
            <ToggleGroupItem value="Physical">Physical</ToggleGroupItem>
            <ToggleGroupItem value="Service">Service</ToggleGroupItem>
          </ToggleGroup>
          <Select
            value={category}
            disabled={pending}
            onValueChange={(value) => {
              setCategory(value);
              setPage(1);
            }}
          >
            <SelectTrigger
              aria-label="Filter by category"
              className="w-full sm:w-44"
            >
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="all">All categories</SelectItem>
                {categories.map((value) => (
                  <SelectItem key={value} value={value}>
                    {categoryLabel(value)}
                  </SelectItem>
                ))}
              </SelectGroup>
            </SelectContent>
          </Select>
          {filtered && (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              disabled={pending}
              onClick={clearFilters}
            >
              Clear filters
            </Button>
          )}
        </DataTableToolbar>
        {query.isError && data && (
          <ErrorState
            title="Refresh failed"
            description="Previous results shown."
            onRetry={() => query.refetch()}
          />
        )}
        {query.isLoading ? (
          <Skeleton className="h-64 w-full" />
        ) : query.isError && !data ? (
          <ErrorState
            title="Catalog load failed"
            onRetry={() => query.refetch()}
          />
        ) : items.length === 0 && page === 1 ? (
          <EmptyState
            icon={<PackageIcon className="size-8 text-muted-foreground" />}
            title={filtered ? 'No matching cost items' : 'No cost items'}
            action={
              filtered ? (
                <Button variant="outline" onClick={clearFilters}>
                  Clear filters
                </Button>
              ) : (
                <Button onClick={() => navigate('/cost-catalog/new')}>
                  <PlusIcon data-icon="inline-start" />
                  Add cost item
                </Button>
              )
            }
          />
        ) : (
          <CostItemsTable
            items={items}
            selectionKey={JSON.stringify([page, pageSize, itemType, category])}
            onPendingChange={setRemovalPending}
            fallbackFocusRef={addRef}
            sorting={{
              ...list.serverSorting,
              disabled: query.isError,
              pending,
            }}
            pagination={{
              page,
              pageSize,
              totalCount: data?.totalCount ?? 0,
              onPageChange: setPage,
              onPageSizeChange: setPageSize,
              disabled: query.isError,
              pending: pending || query.isPlaceholderData,
              label: 'Cost items',
            }}
            onRowClick={(item) => navigate(`/cost-catalog/${item.id}`)}
          />
        )}
      </div>
    </Page>
  );
}
