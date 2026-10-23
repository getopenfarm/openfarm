import { DownloadIcon, Package2Icon, PencilIcon, PlusIcon } from 'lucide-react';
import { useMemo, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { BackLink } from '@/components/back-link/BackLink';
import { DataTableSearch } from '@/components/data-table/DataTableSearch';
import {
  DataTableTabsList,
  DataTableToolbar,
} from '@/components/data-table/DataTableToolbar';
import {
  serverListQueryOptions,
  useRetainedListData,
  useServerPageRecovery,
} from '@/components/data-table/useServerListState';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Swatch } from '@/components/swatch/Swatch';
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
import { Tabs, TabsContent, TabsTrigger } from '@/components/ui/tabs';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { ShelfGroups } from '@/features/spools/components/ShelfGroups';
import { SpoolHistoryTable } from '@/features/spools/components/SpoolHistoryTable';
import { SpoolsTable } from '@/features/spools/components/SpoolsTable';
import { groupShelf } from '@/features/spools/lib/groupShelf';
import { MaterialsReportTable } from '@/features/stats/components/MaterialsReportTable';
import {
  defaultRange,
  RangeFilter,
  rangeInstants,
} from '@/features/stats/components/RangeFilter';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { downloadFile } from '@/lib/api/downloadFile';
import {
  type DailyUsageResponse,
  type ListFilamentShortagesResponse,
  type ListFilamentsResponse,
  type ListSpoolsResponse,
  type SpoolResponse,
  SpoolState,
} from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsFilamentsListFilamentShortagesEndpoint,
  useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint,
} from '@/lib/api/generated/filaments/filaments';
import {
  getOpenFarmApiEndpointsReportsExportMaterialsReportEndpointUrl,
  useOpenFarmApiEndpointsReportsDailyUsageEndpoint,
} from '@/lib/api/generated/reports/reports';
import {
  getOpenFarmApiEndpointsSpoolsExportSpoolHistoryEndpointUrl,
  useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint,
} from '@/lib/api/generated/spools/spools';
import { formatCost } from '@/lib/formatUnits';
import { ColorRows } from './components/ColorRows';
import { StockTiles } from './components/StockTiles';
import { addSpoolUrl } from './lib/addSpoolUrl';

const TABS = ['colors', 'spools', 'usage', 'history'] as const;
type Tab = (typeof TABS)[number];

const ALL = 'all';

const SOURCES = [
  { value: 'Job', label: 'Print' },
  { value: 'Manual', label: 'Manual usage' },
  { value: 'Correction', label: 'Weight correction' },
  { value: 'WriteOff', label: 'Finished' },
  { value: 'Restore', label: 'Restored' },
];

type SpoolView = 'active' | 'finished';

/** Every search term appears in the spool's label or color; vendor and material are the page's. */
function matchesSearch(spool: SpoolResponse, search: string) {
  const text = [spool.label, spool.colorName].join(' ').toLowerCase();
  return search
    .toLowerCase()
    .split(/\s+/)
    .every((term) => text.includes(term));
}

function SectionTitle({ title, count }: { title: string; count: number }) {
  return (
    <h3 className="text-sm font-medium">
      {title}{' '}
      <span className="font-normal text-muted-foreground tabular-nums">
        {count}
      </span>
    </h3>
  );
}

/**
 * One filament: its colors' stock, its spools, its usage and its history, as tabs. The tab and the color filter live in
 * the URL (`?tab=`, `?color=`); the other filters live here, so switching tabs keeps them. Spools in printers and on the shelf
 * load whole; finished ones, which pile up by the thousand, page from the server.
 */
export function FilamentDetailPage() {
  const { id = '' } = useParams<{ id: string }>();
  const [params, setParams] = useSearchParams();
  const [view, setView] = useState<SpoolView>('active');
  const [search, setSearch] = useState('');
  const [range, setRange] = useState(defaultRange);
  const [source, setSource] = useState(ALL);
  const [finishedPage, setFinishedPage] = useState(1);
  const [finishedPageSize, setFinishedPageSize] = useState(25);
  const finishedSearch = useDebouncedValue(search.trim(), 250);
  const timeZone = useMemo(
    () => Intl.DateTimeFormat().resolvedOptions().timeZone,
    [],
  );

  const filamentsQuery =
    useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint();
  const list = filamentsQuery.data?.data as ListFilamentsResponse | undefined;
  const filament = list?.filaments?.find((entry) => entry.id === id);
  const variantIds = new Set((filament?.variants ?? []).map((v) => v.id));
  // A color of another filament (a stale link) filters nothing out.
  const colorParam = params.get('color');
  const color = colorParam && variantIds.has(colorParam) ? colorParam : null;

  const shortages =
    (
      useOpenFarmApiEndpointsFilamentsListFilamentShortagesEndpoint().data
        ?.data as ListFilamentShortagesResponse | undefined
    )?.shortages ?? [];
  const usingQuery = useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint({
    filamentId: id,
    state: SpoolState.Using,
  });
  const shelfQuery = useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint({
    filamentId: id,
    state: SpoolState.InStorage,
  });
  const using =
    (usingQuery.data?.data as ListSpoolsResponse | undefined)?.spools ?? [];
  const onShelf =
    (shelfQuery.data?.data as ListSpoolsResponse | undefined)?.spools ?? [];

  // A new color or search starts the finished list again at page 1.
  const finishedFilterKey = `${color ?? ''}|${finishedSearch}`;
  const [shownFinishedKey, setShownFinishedKey] = useState(finishedFilterKey);
  if (shownFinishedKey !== finishedFilterKey) {
    setShownFinishedKey(finishedFilterKey);
    setFinishedPage(1);
  }
  const finishedQuery = useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint(
    {
      filamentId: id,
      state: SpoolState.Finished,
      newestFirst: true,
      page: finishedPage,
      pageSize: finishedPageSize,
      ...(color ? { filamentVariantId: color } : {}),
      ...(finishedSearch ? { search: finishedSearch } : {}),
    },
    { query: serverListQueryOptions },
  );
  const finished = useRetainedListData(
    finishedQuery.data?.data as ListSpoolsResponse | undefined,
  );
  const finishedTotal = finished?.totalCount ?? 0;
  useServerPageRecovery(
    finishedPage,
    finishedPageSize,
    finishedTotal,
    finishedQuery.isSuccess &&
      !finishedQuery.isFetching &&
      !finishedQuery.isPlaceholderData,
    setFinishedPage,
  );

  const usage = useOpenFarmApiEndpointsReportsDailyUsageEndpoint(
    { filamentId: id, timeZone },
    { query: serverListQueryOptions },
  ).data?.data as DailyUsageResponse | undefined;

  /** Sets or clears one search param without adding a history entry. */
  function setParam(key: 'tab' | 'color', value: string | undefined) {
    setParams(
      (current) => {
        const next = new URLSearchParams(current);
        if (value) next.set(key, value);
        else next.delete(key);
        return next;
      },
      { replace: true },
    );
  }

  const backLink = (
    <BackLink to="/filament" className="mb-1">
      Back to filament
    </BackLink>
  );

  // A filament added a moment ago is missing from the cached list until the refetch lands.
  if (filamentsQuery.isLoading || (!filament && filamentsQuery.isFetching)) {
    return (
      <Page>
        <PageHeader above={backLink} title="Filament" />
        <Skeleton className="h-24 w-full" />
        <Skeleton className="h-64 w-full" />
      </Page>
    );
  }

  if (!filament && filamentsQuery.isError) {
    return (
      <Page>
        <PageHeader above={backLink} title="Filament" />
        <ErrorState
          title="Could not load filament"
          onRetry={() => filamentsQuery.refetch()}
        />
      </Page>
    );
  }

  if (!filament) {
    return (
      <Page>
        <PageHeader above={backLink} title="Filament" />
        <EmptyState
          title="Filament not found"
          description="Removed or invalid link."
          action={
            <Button asChild variant="outline">
              <Link to="/filament">Back to filament</Link>
            </Button>
          }
        />
      </Page>
    );
  }

  const variants = filament.variants ?? [];
  const stock = (list?.stock ?? []).filter((row) =>
    variantIds.has(row.filamentVariantId),
  );

  const tabParam = params.get('tab');
  const tab: Tab = TABS.includes(tabParam as Tab)
    ? (tabParam as Tab)
    : 'colors';

  const matches = (spool: SpoolResponse) =>
    (!color || spool.filamentVariantId === color) &&
    matchesSearch(spool, search);
  const inPrinters = using.filter(matches);
  const shelf = groupShelf(onShelf.filter(matches));
  const shelfCount =
    shelf.full.reduce((total, group) => total + group.spools.length, 0) +
    shelf.partial.length;
  const activeCount = inPrinters.length + shelfCount;
  const spoolsLoading = usingQuery.isLoading || shelfQuery.isLoading;
  const spoolsFailed = usingQuery.isError || shelfQuery.isError;

  const instants = rangeInstants(range);
  const historyFilter = {
    ...instants,
    filamentId: id,
    ...(color ? { filamentVariantId: color } : {}),
    ...(source === ALL ? {} : { source }),
  };

  const colorSelect = (
    <Select
      value={color ?? ALL}
      onValueChange={(next) =>
        setParam('color', next === ALL ? undefined : next)
      }
    >
      <SelectTrigger aria-label="Color" className="w-48">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectGroup>
          <SelectItem value={ALL}>All colors</SelectItem>
          {variants.map((variant) => (
            <SelectItem key={variant.id} value={variant.id ?? ''}>
              <Swatch colorHex={variant.colorHex} size="sm" />
              {variant.colorName}
            </SelectItem>
          ))}
        </SelectGroup>
      </SelectContent>
    </Select>
  );

  const addSpools = (
    <Button asChild>
      <Link to={addSpoolUrl(filament)}>
        <PlusIcon data-icon="inline-start" />
        Add spools
      </Link>
    </Button>
  );

  return (
    <Page>
      <PageHeader
        above={backLink}
        title={`${filament.vendorName} · ${filament.material}`}
        description={[
          filament.materialType,
          filament.pricePerKg == null
            ? null
            : `${formatCost(filament.pricePerKg)}/kg`,
        ]
          .filter(Boolean)
          .join(' · ')}
        action={
          <div className="flex flex-wrap items-center gap-2">
            <Button asChild variant="outline">
              <Link to={`/filament/${id}/edit`}>
                <PencilIcon data-icon="inline-start" />
                Edit
              </Link>
            </Button>
            {addSpools}
          </div>
        }
      />
      <StockTiles stock={stock} size="compact" />
      <Tabs
        value={tab}
        onValueChange={(next) =>
          setParam('tab', next === 'colors' ? undefined : next)
        }
        className="gap-4"
      >
        <DataTableToolbar
          search={
            tab === 'spools' ? (
              <DataTableSearch
                value={search}
                onValueChange={setSearch}
                label="Search spools by label or color"
                placeholder="Search spools…"
              />
            ) : undefined
          }
          actions={
            tab === 'usage' || tab === 'history' ? (
              <Button
                variant="outline"
                onClick={() =>
                  tab === 'usage'
                    ? downloadFile(
                        getOpenFarmApiEndpointsReportsExportMaterialsReportEndpointUrl(
                          { ...instants, filamentId: id },
                        ),
                        'materials-report.csv',
                        'Could not export CSV.',
                      )
                    : downloadFile(
                        getOpenFarmApiEndpointsSpoolsExportSpoolHistoryEndpointUrl(
                          historyFilter,
                        ),
                        'spool-history.csv',
                        'Could not export CSV.',
                      )
                }
              >
                <DownloadIcon data-icon="inline-start" />
                Export CSV
              </Button>
            ) : undefined
          }
        >
          <DataTableTabsList aria-label="Filament view">
            <TabsTrigger value="colors">Colors</TabsTrigger>
            <TabsTrigger value="spools">Spools</TabsTrigger>
            <TabsTrigger value="usage">Usage</TabsTrigger>
            <TabsTrigger value="history">History</TabsTrigger>
          </DataTableTabsList>
          {tab === 'spools' && (
            <>
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
                    {activeCount}
                  </span>
                </ToggleGroupItem>
                <ToggleGroupItem value="finished">
                  Finished
                  <span className="ml-1 tabular-nums opacity-70">
                    {finishedTotal}
                  </span>
                </ToggleGroupItem>
              </ToggleGroup>
              {colorSelect}
            </>
          )}
          {(tab === 'usage' || tab === 'history') && (
            <RangeFilter value={range} onChange={setRange} />
          )}
          {tab === 'history' && (
            <>
              {colorSelect}
              <Select value={source} onValueChange={setSource}>
                <SelectTrigger aria-label="Source" className="w-44">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value={ALL}>All sources</SelectItem>
                    {SOURCES.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
            </>
          )}
        </DataTableToolbar>
        <TabsContent value="colors">
          <ColorRows
            filament={filament}
            stock={stock}
            shortages={shortages}
            usage={usage}
            spools={[...using, ...onShelf]}
          />
        </TabsContent>
        <TabsContent value="spools">
          {view === 'finished' ? (
            finishedQuery.isLoading ? (
              <Skeleton className="h-64 w-full" />
            ) : finishedQuery.isError && !finished ? (
              <ErrorState
                title="Could not load spools"
                onRetry={() => finishedQuery.refetch()}
              />
            ) : finishedTotal === 0 ? (
              <EmptyState
                title={
                  color || finishedSearch
                    ? 'No spools match'
                    : 'No finished spools'
                }
                description={
                  color || finishedSearch
                    ? 'Change the search or filter.'
                    : undefined
                }
              />
            ) : (
              <SpoolsTable
                spools={finished?.spools ?? []}
                serverPagination={{
                  page: finishedPage,
                  pageSize: finishedPageSize,
                  totalCount: finishedTotal,
                  onPageChange: setFinishedPage,
                  onPageSizeChange: (size) => {
                    setFinishedPageSize(size);
                    setFinishedPage(1);
                  },
                  label: 'Finished spools',
                  pending: finishedQuery.isFetching,
                }}
              />
            )
          ) : spoolsLoading ? (
            <Skeleton className="h-64 w-full" />
          ) : spoolsFailed ? (
            <ErrorState
              title="Could not load spools"
              onRetry={() => {
                usingQuery.refetch();
                shelfQuery.refetch();
              }}
            />
          ) : using.length + onShelf.length === 0 ? (
            <EmptyState
              icon={<Package2Icon className="size-8 text-muted-foreground" />}
              title="No spools"
              description="Add spools to track this filament."
              action={addSpools}
            />
          ) : activeCount === 0 ? (
            <EmptyState
              title="No spools match"
              description="Change the search or filter."
            />
          ) : (
            <div className="flex flex-col gap-6">
              {inPrinters.length > 0 && (
                <section className="flex flex-col gap-2">
                  <SectionTitle title="In printers" count={inPrinters.length} />
                  <SpoolsTable spools={inPrinters} selectable />
                </section>
              )}
              {shelfCount > 0 && (
                <section className="flex flex-col gap-2">
                  <SectionTitle title="On the shelf" count={shelfCount} />
                  {shelf.full.length > 0 && <ShelfGroups groups={shelf.full} />}
                  {shelf.partial.length > 0 && (
                    <>
                      <h4 className="pt-2 text-xs text-muted-foreground">
                        Partial · {shelf.partial.length}
                      </h4>
                      <SpoolsTable spools={shelf.partial} selectable />
                    </>
                  )}
                </section>
              )}
            </div>
          )}
        </TabsContent>
        <TabsContent value="usage">
          <MaterialsReportTable range={instants} filamentId={id} />
        </TabsContent>
        <TabsContent value="history">
          <SpoolHistoryTable filter={historyFilter} />
        </TabsContent>
      </Tabs>
    </Page>
  );
}
