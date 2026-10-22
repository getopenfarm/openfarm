import { Package2Icon, PlusIcon } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { DataTableSearch } from '@/components/data-table/DataTableSearch';
import { DataTableToolbar } from '@/components/data-table/DataTableToolbar';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import type {
  ListSpoolsResponse,
  SpoolResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint } from '@/lib/api/generated/spools/spools';
import { pluralize } from '@/lib/pluralize';
import { SpoolsTable } from './components/SpoolsTable';

const LOW_REMAINING_FRACTION = 0.25;

type SpoolView = 'active' | 'finished';

/** Every search term appears in the spool's label, vendor, material or color. */
function matchesSearch(spool: SpoolResponse, search: string) {
  const text = [spool.label, spool.vendorName, spool.material, spool.colorName]
    .join(' ')
    .toLowerCase();
  return search
    .toLowerCase()
    .split(/\s+/)
    .every((term) => text.includes(term));
}

export function SpoolsPage() {
  const navigate = useNavigate();
  const [view, setView] = useState<SpoolView>('active');
  const [search, setSearch] = useState('');
  const spoolsQuery = useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint();
  const spools =
    (spoolsQuery.data?.data as ListSpoolsResponse | undefined)?.spools ?? [];
  const finishedCount = spools.filter((s) => s.state === 'Finished').length;
  const visible = spools.filter(
    (spool) =>
      (spool.state === 'Finished') === (view === 'finished') &&
      matchesSearch(spool, search),
  );

  const inUseCount = spools.filter((spool) => spool.state === 'Using').length;
  const lowCount = spools.filter(
    (spool) =>
      spool.state === 'Using' &&
      (spool.remainingWeightGrams ?? 0) / (spool.spoolWeightGrams || 1) <
        LOW_REMAINING_FRACTION,
  ).length;

  return (
    <Page>
      <PageHeader
        title="Spools"
        description={
          <>
            Physical inventory · {pluralize(spools.length, 'spool')} ·{' '}
            <span className="text-kind-green font-medium">
              {inUseCount} in use
            </span>{' '}
            · <span className="text-kind-red font-medium">{lowCount} low</span>
          </>
        }
        action={
          <Button onClick={() => navigate('/spools/new')}>
            <PlusIcon data-icon="inline-start" />
            Add spool
          </Button>
        }
      />
      {spoolsQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : spoolsQuery.isError ? (
        <ErrorState
          title="Could not load spools"
          onRetry={() => spoolsQuery.refetch()}
        />
      ) : spools.length === 0 ? (
        <EmptyState
          icon={<Package2Icon className="size-8 text-muted-foreground" />}
          title="No spools"
          description="Add a spool to track inventory."
          action={
            <Button onClick={() => navigate('/spools/new')}>Add spool</Button>
          }
        />
      ) : (
        <div className="flex flex-col gap-4">
          <DataTableToolbar
            search={
              <DataTableSearch
                value={search}
                onValueChange={setSearch}
                label="Search spools by label, vendor, material or color"
                placeholder="Search spools…"
              />
            }
          >
            <ToggleGroup
              aria-label="Show spools"
              type="single"
              variant="outline"
              value={view}
              onValueChange={(next) => {
                if (next) setView(next as SpoolView);
              }}
            >
              <ToggleGroupItem value="active">
                Active
                <span className="ml-1 tabular-nums opacity-70">
                  {spools.length - finishedCount}
                </span>
              </ToggleGroupItem>
              <ToggleGroupItem value="finished">
                Finished
                <span className="ml-1 tabular-nums opacity-70">
                  {finishedCount}
                </span>
              </ToggleGroupItem>
            </ToggleGroup>
          </DataTableToolbar>
          {visible.length === 0 ? (
            <EmptyState
              title="No spools match"
              description="Change the search or filter."
            />
          ) : (
            <SpoolsTable spools={visible} selectable={view === 'active'} />
          )}
        </div>
      )}
    </Page>
  );
}
