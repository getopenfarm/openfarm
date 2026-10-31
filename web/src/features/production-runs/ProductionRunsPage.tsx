import type { ColumnDef } from '@tanstack/react-table';
import { ListChecksIcon, PlusIcon } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { DataTableSearch } from '@/components/data-table/DataTableSearch';
import {
  DataTableTabsList,
  DataTableToolbar,
} from '@/components/data-table/DataTableToolbar';
import {
  serverListQueryOptions,
  useRetainedListData,
  useServerListState,
  useServerPageRecovery,
} from '@/components/data-table/useServerListState';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { PermissionMenuItem as DropdownMenuItem } from '@/components/PermissionMenuItem';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { DropdownMenuSeparator } from '@/components/ui/dropdown-menu';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsTrigger } from '@/components/ui/tabs';
import type {
  ListProductionRunsResponse,
  ProductionRunSummaryResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsProductionRunsListProductionRunsEndpoint } from '@/lib/api/generated/production-runs/production-runs';
import { cn } from '@/lib/utils';
import { PlateStrip } from './components/PlateStrip';
import { useProductionHub } from './hooks/useProductionHub';
import { useRunActions } from './hooks/useRunActions';
import { describeDue } from './lib/dueDate';
import { canCloseRun, isOpenRun, RUN_STATUS } from './lib/runStatus';

const TABS = [
  { value: 'Open', label: 'Open' },
  { value: 'Closed', label: 'Closed' },
  { value: 'Cancelled', label: 'Cancelled' },
] as const;
type Tab = (typeof TABS)[number]['value'];

const units = (n?: number) => Number((n ?? 0).toFixed(2));

/** "Bracket ×20 · Lamp ×5 · +2": what a named run holds, since its name does not say. */
function linesSummary(run: ProductionRunSummaryResponse) {
  const lines = run.lines ?? [];
  const shown = lines
    .slice(0, 2)
    .map((l) =>
      l.quantity == null ? l.targetName : `${l.targetName} ×${l.quantity}`,
    );
  return [...shown, ...(lines.length > 2 ? [`+${lines.length - 2}`] : [])].join(
    ' · ',
  );
}

function RunProgress({ run }: { run: ProductionRunSummaryResponse }) {
  const finite = (run.targetUnits ?? 0) > 0;
  return (
    <div className="flex min-w-48 flex-col gap-1.5">
      {finite && <PlateStrip plates={run.plates ?? {}} size="sm" />}
      <span className="text-xs text-muted-foreground tabular-nums">
        {finite &&
          `${units(run.completedUnits)}/${units(run.targetUnits)} units`}
        {finite &&
          (run.writtenOffUnits ?? 0) > 0 &&
          ` · ${run.writtenOffUnits} written off`}
        {finite && (run.producedUnits ?? 0) > 0 && ' · '}
        {(run.producedUnits ?? 0) > 0 && `${run.producedUnits} backfill units`}
        {!finite && (run.producedUnits ?? 0) === 0 && 'Backfill, none yet'}
      </span>
    </div>
  );
}

function Due({ run }: { run: ProductionRunSummaryResponse }) {
  if (!run.dueDate) return <span className="text-muted-foreground">—</span>;
  const due = describeDue(run.dueDate);
  const late = due.overdue && isOpenRun(run.status);
  return (
    <div className="flex flex-col">
      <span className="tabular-nums">{due.label}</span>
      {isOpenRun(run.status) && (
        <span
          className={cn(
            'text-xs',
            late
              ? 'font-medium text-status-attention'
              : due.soon
                ? 'text-foreground'
                : 'text-muted-foreground',
          )}
        >
          {due.relative}
        </span>
      )}
    </div>
  );
}

function Attention({ run }: { run: ProductionRunSummaryResponse }) {
  const failed = run.plates?.needsAction ?? 0;
  const blocked = run.blockedCount ?? 0;
  if (failed === 0 && blocked === 0) return null;
  return (
    <div className="flex flex-col text-xs">
      {failed > 0 && (
        <span className="font-medium text-destructive">{failed} failed</span>
      )}
      {blocked > 0 && (
        <span className="text-status-attention">{blocked} send failed</span>
      )}
    </div>
  );
}

/** Orders and batches: what to make, how far along, and what needs a decision. The queue is the dispatch view. */
export function ProductionRunsPage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const tabParam = params.get('tab');
  const tab: Tab = TABS.some((t) => t.value === tabParam)
    ? (tabParam as Tab)
    : 'Open';
  const [search, setSearch] = useState('');
  const list = useServerListState('due', 'dueDate', true);
  const { page, pageSize, setPage, setPageSize } = list;
  const actions = useRunActions();
  useProductionHub();

  const runsQuery =
    useOpenFarmApiEndpointsProductionRunsListProductionRunsEndpoint(
      { ...list.params, status: tab, search: search.trim() || undefined },
      { query: serverListQueryOptions },
    );
  const listData = useRetainedListData(
    runsQuery.data?.data as ListProductionRunsResponse | undefined,
  );
  const runs = listData?.items ?? [];
  const counts = listData?.statusCounts;
  useServerPageRecovery(
    page,
    pageSize,
    listData?.totalCount ?? 0,
    runsQuery.isSuccess &&
      !runsQuery.isFetching &&
      !runsQuery.isPlaceholderData,
    setPage,
  );

  function setTab(next: string) {
    setParams(
      (current) => {
        const updated = new URLSearchParams(current);
        if (next === 'Open') updated.delete('tab');
        else updated.set('tab', next);
        return updated;
      },
      { replace: true },
    );
    setPage(1);
  }

  const columns: ColumnDef<ProductionRunSummaryResponse>[] = [
    {
      id: 'run',
      header: 'Run',
      cell: ({ row }) => {
        const run = row.original;
        const status = RUN_STATUS[run.status ?? ''];
        return (
          <div className="flex min-w-0 flex-col gap-0.5">
            <span className="flex items-center gap-2">
              <Link
                to={`/production-runs/${run.id}`}
                className="truncate font-medium hover:underline"
              >
                {run.displayLabel}
              </Link>
              {run.status === 'Complete' && status && (
                <StatusBadge kind={status.kind} label={status.label} />
              )}
            </span>
            {run.name && (
              <span className="truncate text-xs text-muted-foreground">
                {linesSummary(run)}
              </span>
            )}
          </div>
        );
      },
    },
    {
      id: 'progress',
      header: 'Progress',
      cell: ({ row }) => <RunProgress run={row.original} />,
    },
    {
      id: 'due',
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Due" />
      ),
      cell: ({ row }) => <Due run={row.original} />,
    },
    {
      id: 'attention',
      header: 'Needs attention',
      cell: ({ row }) => <Attention run={row.original} />,
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      cell: ({ row }) => {
        const run = row.original;
        const id = run.id ?? '';
        const open = isOpenRun(run.status);
        return (
          <DataTableRowActions label={run.displayLabel ?? 'run'}>
            <DropdownMenuItem
              onSelect={() => navigate(`/production-runs/${id}`)}
            >
              Open
            </DropdownMenuItem>
            {open && (run.plates?.queued ?? 0) > 0 && (
              <DropdownMenuItem
                permission="print-jobs:reorder"
                onSelect={() => actions.moveToTop(id)}
              >
                Move to top of queue
              </DropdownMenuItem>
            )}
            {open && (
              <DropdownMenuItem
                permission="production-runs:edit"
                onSelect={() => navigate(`/production-runs/${id}/edit`)}
              >
                Edit
              </DropdownMenuItem>
            )}
            {canCloseRun(run.status, (run.targetUnits ?? 0) > 0) && (
              <DropdownMenuItem
                permission="production-runs:close"
                onSelect={() => actions.close(id)}
              >
                Close run
              </DropdownMenuItem>
            )}
            {open && (
              <>
                <DropdownMenuSeparator />
                <DropdownMenuItem
                  permission="production-runs:cancel"
                  variant="destructive"
                  onSelect={() => actions.cancel(id, run.displayLabel ?? 'run')}
                >
                  Cancel run
                </DropdownMenuItem>
              </>
            )}
          </DataTableRowActions>
        );
      },
    },
  ];

  const empty = runs.length === 0 && page === 1;
  return (
    <Page>
      <PageHeader
        title="Runs"
        description="Orders and batches · progress updates live"
        action={
          <Button
            permission="production-runs:create"
            onClick={() => navigate('/production-runs/new')}
          >
            <PlusIcon data-icon="inline-start" /> Add run
          </Button>
        }
      />

      <Tabs value={tab} onValueChange={setTab} className="gap-4">
        <DataTableToolbar
          search={
            <DataTableSearch
              label="Search runs by name or part"
              placeholder="Search runs…"
              value={search}
              onValueChange={(value) => {
                setSearch(value);
                setPage(1);
              }}
            />
          }
        >
          <DataTableTabsList aria-label="Run status">
            {TABS.map(({ value, label }) => (
              <TabsTrigger key={value} value={value}>
                {label}
                <span className="ml-1 tabular-nums opacity-70">
                  {counts?.[value] ?? '—'}
                </span>
              </TabsTrigger>
            ))}
          </DataTableTabsList>
        </DataTableToolbar>

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
            title="Could not load runs"
            onRetry={() => runsQuery.refetch()}
          />
        ) : empty && search.trim() ? (
          <EmptyState size="sm" title="No runs match search" />
        ) : empty && tab !== 'Open' ? (
          <EmptyState
            size="sm"
            title={tab === 'Closed' ? 'No closed runs' : 'No cancelled runs'}
          />
        ) : empty ? (
          <EmptyState
            icon={<ListChecksIcon className="size-8 text-muted-foreground" />}
            title="No open runs"
            description="Add a run, or send a part or assembly to the queue."
            action={
              <Button
                permission="production-runs:create"
                onClick={() => navigate('/production-runs/new')}
              >
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
              onPageSizeChange: setPageSize,
              disabled: runsQuery.isError,
              pending: runsQuery.isFetching || runsQuery.isPlaceholderData,
            }}
            columns={columns}
            data={runs}
            getRowId={(row) => row.id ?? ''}
            onRowClick={(row) => navigate(`/production-runs/${row.id}`)}
          />
        )}
      </Tabs>
      {actions.cancelDialog}
    </Page>
  );
}
