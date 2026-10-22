import { Package2Icon, PlusIcon } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { DataTableSearch } from '@/components/data-table/DataTableSearch';
import { DataTableToolbar } from '@/components/data-table/DataTableToolbar';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { KpiCard } from '@/components/kpi-card/KpiCard';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { sumStock } from '@/features/filament/lib/stock';
import type {
  ListFilamentsResponse,
  ListSpoolsResponse,
  SpoolResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint } from '@/lib/api/generated/filaments/filaments';
import { useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint } from '@/lib/api/generated/spools/spools';
import { formatCost, formatWeight } from '@/lib/formatUnits';
import { pluralize } from '@/lib/pluralize';
import { SpoolsTable } from './components/SpoolsTable';

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
  const filamentsQuery =
    useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint();
  const stock = sumStock(
    (filamentsQuery.data?.data as ListFilamentsResponse | undefined)?.stock ??
      [],
  );
  const visible = spools.filter(
    (spool) =>
      (spool.state === 'Finished') === (view === 'finished') &&
      matchesSearch(spool, search),
  );

  const inUseCount = spools.filter((spool) => spool.state === 'Using').length;

  return (
    <Page>
      <PageHeader
        title="Spools"
        description="Physical inventory"
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
          <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
            <KpiCard
              label="On hand"
              value={formatWeight(stock.onHandGrams)}
              dotClassName={stock.lowColors > 0 ? 'bg-kind-amber' : undefined}
              sub={
                stock.lowColors > 0
                  ? `${pluralize(stock.lowColors, 'color')} low`
                  : undefined
              }
            />
            <KpiCard
              label="Value"
              value={formatCost(stock.value)}
              sub={
                stock.unpricedSpools > 0
                  ? `Partial · ${pluralize(stock.unpricedSpools, 'spool')} unpriced`
                  : undefined
              }
            />
            <KpiCard
              label="Active spools"
              value={stock.activeSpools}
              sub={`${inUseCount} in use`}
            />
            <KpiCard label="Used 30 d" value={formatWeight(stock.usedGrams)} />
          </div>
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
