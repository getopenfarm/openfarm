import type { ColumnDef, VisibilityState } from '@tanstack/react-table';
import {
  ActivityIcon,
  HistoryIcon,
  KeyRoundIcon,
  ShapesIcon,
  UserIcon,
  XIcon,
} from 'lucide-react';
import { createContext, useContext, useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router';
import {
  DataTable,
  type DataTableRowContainerProps,
} from '@/components/data-table/DataTable';
import {
  DataTableTabsList,
  DataTableToolbar,
} from '@/components/data-table/DataTableToolbar';
import {
  serverListQueryOptions,
  useRetainedListData,
} from '@/components/data-table/useServerListState';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { TableRow } from '@/components/ui/table';
import { Tabs, TabsContent, TabsTrigger } from '@/components/ui/tabs';
import {
  useOpenFarmApiEndpointsActivityLogGetAuditFilterOptionsEndpoint,
  useOpenFarmApiEndpointsActivityLogGetAuditLogEndpoint,
} from '@/lib/api/generated/audit-log/audit-log';
import type { AuditEntryResponse } from '@/lib/api/generated/endpoints.schemas';
import { formatDateTime } from '@/lib/formatDateTime';
import { cn } from '@/lib/utils';
import { AuditDateFilter, AuditFilterMenu } from './AuditFilters';
import {
  ACTION_LABELS,
  auditActionKind,
  auditActor,
  auditActorLine,
  auditSummary,
  CHANNEL_LABELS,
  humanize,
  isAgent,
} from './auditFormat';

function dateInstant(value: string | null, end: boolean) {
  if (!value) return undefined;
  const date = new Date(`${value}T00:00:00`);
  if (end) date.setDate(date.getDate() + 1);
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString();
}
function AuditTimeLink({ entry }: { entry: AuditEntryResponse }) {
  const location = useLocation();
  return (
    <Link
      className="whitespace-nowrap underline-offset-4 hover:underline"
      to={`/audit-log/${entry.id}`}
      state={{ back: `${location.pathname}${location.search}` }}
    >
      {entry.timestampUtc ? formatDateTime(entry.timestampUtc) : '—'}
    </Link>
  );
}
const agentRows = createContext<ReadonlySet<string>>(new Set());
function AuditRow({
  rowId,
  view,
  selected,
  className,
  ...props
}: DataTableRowContainerProps) {
  const agent = useContext(agentRows).has(rowId);
  return view === 'table' ? (
    <TableRow
      {...props}
      data-agent={agent || undefined}
      data-state={selected ? 'selected' : undefined}
      className={cn(
        className,
        agent && '[&>td:first-child]:shadow-[inset_3px_0_0_var(--primary)]',
      )}
    />
  ) : (
    <li {...props} className={className} />
  );
}
const columns: ColumnDef<AuditEntryResponse>[] = [
  {
    id: 'when',
    header: 'When',
    enableHiding: false,
    cell: ({ row: { original: e } }) => <AuditTimeLink entry={e} />,
  },
  {
    id: 'who',
    header: 'Who',
    cell: ({ row: { original: e } }) => (
      <div className="flex min-w-0 flex-col items-start gap-1">
        <span>{auditActor(e)}</span>
        {e.channel !== 'Browser' && (
          <Badge
            kind={isAgent(e) ? 'blue' : 'zinc'}
            className="max-w-52"
            title={auditActorLine(e)}
          >
            <span className="truncate">
              {e.credentialLabel ??
                CHANNEL_LABELS[e.channel ?? ''] ??
                'Unknown channel'}
            </span>
          </Badge>
        )}
      </div>
    ),
  },
  {
    id: 'what',
    header: 'What',
    cell: ({ row: { original: e } }) => (
      <div className="flex min-w-0 flex-col gap-1">
        <span className="text-xs text-muted-foreground">
          {humanize(e.entityType)}
        </span>
        <span
          className="max-w-72 truncate font-medium"
          title={e.entityLabel ?? e.entityId}
        >
          {e.entityLabel ?? `#${e.entityId}`}
        </span>
      </div>
    ),
  },
  {
    id: 'change',
    header: 'Change',
    cell: ({ row: { original: e } }) => (
      <div className="flex min-w-0 flex-col items-start gap-1">
        <StatusBadge
          kind={auditActionKind(e.action)}
          label={ACTION_LABELS[e.action ?? ''] ?? 'Historical event'}
        />
        {(e.action === 'update' || !e.action || e.action === 'action') && (
          <span
            className="max-w-md truncate text-xs text-muted-foreground"
            title={auditSummary(e)}
          >
            {auditSummary(e)}
          </span>
        )}
      </div>
    ),
  },
];

type View = 'all' | 'agents';
export function AuditLogPage() {
  const [params, setParams] = useSearchParams();
  const navigate = useNavigate();
  const parsedPage = Number(params.get('page'));
  const page =
    Number.isInteger(parsedPage) && parsedPage > 0
      ? Math.min(parsedPage, 100_000)
      : 1;
  const size = Number(params.get('pageSize'));
  const pageSize = [10, 25, 50, 100].includes(size) ? size : 25;
  const channel = params.get('channel') ?? '';
  const view: View = ['Agents', 'Token', 'App'].includes(channel)
    ? 'agents'
    : 'all';
  const pages = useRef({
    all: { page: 1, pageSize: 25 },
    agents: { page: 1, pageSize: 25 },
  });
  useEffect(() => {
    pages.current[view] = { page, pageSize };
  }, [view, page, pageSize]);
  const [visibility, setVisibility] = useState<VisibilityState>({});
  const [openFilter, setOpenFilter] = useState<string | null>(null);
  const filterOpen = (key: string) => ({
    open: openFilter === key,
    onOpenChange: (open: boolean) =>
      setOpenFilter((previous) =>
        open ? key : previous === key ? null : previous,
      ),
  });
  const change = (values: Record<string, string>, reset = true) => {
    if (reset) {
      pages.current.all.page = 1;
      pages.current.agents.page = 1;
    }
    setParams(
      (old) => {
        const next = new URLSearchParams(old);
        for (const [key, value] of Object.entries(values))
          value ? next.set(key, value) : next.delete(key);
        if (reset) next.delete('page');
        return next;
      },
      { replace: true, preventScrollReset: true },
    );
  };
  const switchView = (next: string) => {
    const remembered = pages.current[next as View];
    change(
      {
        channel: next === 'agents' ? 'Agents' : '',
        page: String(remembered.page),
        pageSize: String(remembered.pageSize),
      },
      false,
    );
  };
  const clear = () => {
    pages.current.all.page = 1;
    pages.current.agents.page = 1;
    const allPageSize = pages.current.all.pageSize;
    setParams(allPageSize === 25 ? {} : { pageSize: String(allPageSize) }, {
      replace: true,
      preventScrollReset: true,
    });
  };
  const query = useOpenFarmApiEndpointsActivityLogGetAuditLogEndpoint(
    {
      page,
      pageSize,
      actorId: params.get('actorId') || undefined,
      channel: channel || undefined,
      credentialId: params.get('credentialId') || undefined,
      entityType: params.get('entityType') || undefined,
      entityId: params.get('entityId') || undefined,
      action: params.get('action') || undefined,
      correlationId: params.get('correlationId') || undefined,
      from: dateInstant(params.get('from'), false),
      to: dateInstant(params.get('to'), true),
    },
    { query: { ...serverListQueryOptions, refetchInterval: 15_000 } },
  );
  const optionsQuery =
    useOpenFarmApiEndpointsActivityLogGetAuditFilterOptionsEndpoint();
  const options =
    optionsQuery.data?.status === 200 ? optionsQuery.data.data : undefined;
  const data = useRetainedListData(
    query.data?.status === 200 ? query.data.data : undefined,
  );
  const active = [...params.keys()].some(
    (key) => !['page', 'pageSize'].includes(key),
  );
  const pending = query.isFetching || query.isError || query.isPlaceholderData;
  const credentialLabel = options?.credentials?.find(
    (c) => c.id === params.get('credentialId'),
  )?.label;
  const section = (
    label: string,
    key: string,
    choices: [string, string][],
  ) => ({
    label,
    value:
      key === 'credentialId' && !choices.some(([id]) => id === params.get(key))
        ? ''
        : (params.get(key) ?? ''),
    options: choices,
    onChange: (value: string) => change({ [key]: value }),
  });
  const filters = (
    <>
      {active && (
        <Button variant="ghost" size="sm" onClick={clear}>
          <XIcon data-icon="inline-start" />
          Clear filters
        </Button>
      )}
      <AuditFilterMenu
        label="Channel"
        icon={ActivityIcon}
        {...filterOpen('channel')}
        sections={[
          section('Came through', 'channel', Object.entries(CHANNEL_LABELS)),
        ]}
      />
      <AuditFilterMenu
        label={credentialLabel ?? 'Token or app'}
        icon={KeyRoundIcon}
        {...filterOpen('credential')}
        sections={[
          section(
            'Access tokens',
            'credentialId',
            (options?.credentials ?? [])
              .filter((c) => c.channel === 'Token')
              .map((c) => [c.id ?? '', c.label ?? 'Unknown token']),
          ),
          section(
            'Connected apps',
            'credentialId',
            (options?.credentials ?? [])
              .filter((c) => c.channel === 'App')
              .map((c) => [c.id ?? '', c.label ?? 'Unknown app']),
          ),
        ]}
      />
      <AuditFilterMenu
        label="Person"
        icon={UserIcon}
        {...filterOpen('person')}
        sections={[
          section(
            'Changed by',
            'actorId',
            (options?.actors ?? []).map((a) => [
              a.id ?? '',
              a.name ?? 'Unknown historical user',
            ]),
          ),
        ]}
      />
      <AuditFilterMenu
        label="Type"
        icon={ShapesIcon}
        {...filterOpen('type')}
        sections={[
          section(
            'What changed',
            'entityType',
            (options?.entityTypes ?? [])
              .map((type) => [type, humanize(type)])
              .sort((a, b) => a[1].localeCompare(b[1])) as [string, string][],
          ),
          section(
            'Action',
            'action',
            (options?.actions ?? []).map((action) => [
              action,
              ACTION_LABELS[action] ?? humanize(action),
            ]),
          ),
        ]}
      />
      <AuditDateFilter
        from={params.get('from') ?? ''}
        to={params.get('to') ?? ''}
        {...filterOpen('date')}
        onChange={(from, to) => change({ from, to })}
      />
    </>
  );
  const table = (
    <agentRows.Provider
      value={
        new Set((data?.items ?? []).filter(isAgent).map((e) => e.id ?? ''))
      }
    >
      <DataTable
        columns={columns}
        data={data?.items ?? []}
        getRowId={(row) => row.id ?? ''}
        rowContainer={AuditRow}
        toolbar={null}
        viewOptionsLabel="Fields"
        columnVisibility={visibility}
        onColumnVisibilityChange={setVisibility}
        onRowClick={(row) =>
          navigate(`/audit-log/${row.id}`, {
            state: { back: `/audit-log?${params}` },
          })
        }
        emptyState={
          <EmptyState
            icon={<HistoryIcon />}
            title={active ? 'No changes match your filters' : 'No changes yet'}
            description={
              active
                ? 'Try a wider date range or fewer filters.'
                : 'Recorded changes across the farm appear here.'
            }
            bordered={false}
            action={
              active ? (
                <Button variant="outline" size="sm" onClick={clear}>
                  Clear filters
                </Button>
              ) : undefined
            }
          />
        }
        renderContent={(content) =>
          query.isLoading ? (
            <div
              className="flex flex-col gap-3 p-4"
              role="status"
              aria-label="Loading audit log"
            >
              <Skeleton className="h-6 w-full" />
              <Skeleton className="h-14 w-full" />
              <Skeleton className="h-14 w-full" />
            </div>
          ) : query.isError && !data ? (
            <ErrorState
              description="Could not load audit log."
              onRetry={() => query.refetch()}
              bordered={false}
            />
          ) : (
            content
          )
        }
        serverPagination={{
          page,
          pageSize,
          totalCount: data?.totalCount ?? 0,
          label: 'Audit log',
          pending,
          disabled: pending,
          onPageChange: (next) => {
            pages.current[view].page = next;
            change({ page: String(next) }, false);
          },
          onPageSizeChange: (next) => {
            pages.current[view] = { page: 1, pageSize: next };
            change({ pageSize: String(next), page: '1' }, false);
          },
        }}
      />
    </agentRows.Provider>
  );
  return (
    <Page>
      <PageHeader
        title="Audit log"
        description="Every change, who made it, and whether it came through the browser, a token, or a connected app."
      />
      <Tabs value={view} onValueChange={switchView} className="gap-4">
        <DataTableToolbar actions={filters}>
          <DataTableTabsList aria-label="Audit views">
            <TabsTrigger value="all">Everything</TabsTrigger>
            <TabsTrigger value="agents">Tokens and apps</TabsTrigger>
          </DataTableTabsList>
        </DataTableToolbar>
        {(params.get('entityId') || params.get('correlationId')) && (
          <div className="flex flex-wrap items-center gap-3 text-sm text-muted-foreground">
            <span>
              {params.get('correlationId')
                ? 'Showing one request’s changes'
                : `Showing one ${humanize(params.get('entityType')).toLowerCase()}’s changes`}
            </span>
            <Button
              variant="link"
              size="sm"
              onClick={() => change({ entityId: '', correlationId: '' })}
            >
              Show all
            </Button>
          </div>
        )}
        {optionsQuery.isError && (
          <ErrorState
            description="Could not load audit filters."
            onRetry={() => optionsQuery.refetch()}
          />
        )}
        {query.isError && data && (
          <ErrorState
            description="Could not refresh audit log."
            onRetry={() => query.refetch()}
          />
        )}
        <TabsContent value="all">{table}</TabsContent>
        <TabsContent value="agents">{table}</TabsContent>
      </Tabs>
    </Page>
  );
}
