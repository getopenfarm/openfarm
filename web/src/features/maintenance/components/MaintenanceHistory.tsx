import { keepPreviousData } from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { HistoryIcon } from 'lucide-react';
import { useState } from 'react';
import { DataTable } from '@/components/data-table/DataTable';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Skeleton } from '@/components/ui/skeleton';
import { useCurrency } from '@/features/settings/generalSettings';
import type {
  MaintenanceLogsResponse,
  MaintenanceLogView,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsMaintenanceListMaintenanceLogsEndpoint } from '@/lib/api/generated/maintenance/maintenance';
import { formatDate } from '@/lib/formatDateTime';
import { formatCost, formatHours } from '@/lib/formatUnits';
import { pluralize } from '@/lib/pluralize';

const columns = (currency: string): ColumnDef<MaintenanceLogView>[] => [
  {
    id: 'performedAt',
    header: 'Done',
    meta: { label: 'Done' },
    cell: ({ row }) => (
      <span className="whitespace-nowrap">
        {formatDate(row.original.performedAtUtc ?? '')}
      </span>
    ),
  },
  { id: 'printer', header: 'Printer', accessorKey: 'printerName' },
  { id: 'task', header: 'Task', accessorKey: 'taskName' },
  {
    id: 'by',
    header: 'By',
    meta: { label: 'By' },
    cell: ({ row }) =>
      [row.original.performedByName, row.original.performedByNote]
        .filter(Boolean)
        .join(' · ') || '—',
  },
  {
    id: 'usage',
    header: 'Usage',
    meta: { label: 'Usage' },
    cell: ({ row }) => (
      <span className="font-mono text-xs whitespace-nowrap">
        {formatHours(row.original.hoursAtCompletion ?? 0)} ·{' '}
        {pluralize(row.original.printsAtCompletion ?? 0, 'print')}
      </span>
    ),
  },
  {
    id: 'cost',
    header: 'Cost',
    meta: { label: 'Cost' },
    cell: ({ row }) =>
      row.original.cost == null ? '—' : formatCost(row.original.cost, currency),
  },
  {
    id: 'notes',
    header: 'Notes',
    meta: { label: 'Notes' },
    cell: ({ row }) => (
      <span className="line-clamp-2 text-sm text-muted-foreground">
        {row.original.notes || '—'}
      </span>
    ),
  },
];

/** Completion logs, newest first; kept after a task or printer is gone. */
export function MaintenanceHistory() {
  const currency = useCurrency();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const query = useOpenFarmApiEndpointsMaintenanceListMaintenanceLogsEndpoint(
    { page, pageSize },
    { query: { placeholderData: keepPreviousData } },
  );
  const data = query.data?.data as MaintenanceLogsResponse | undefined;
  if (query.isLoading) return <Skeleton className="h-64 w-full" />;
  if (query.isError && !data)
    return (
      <ErrorState
        title="Could not load history"
        onRetry={() => query.refetch()}
      />
    );
  if (!data?.items?.length && page === 1)
    return (
      <EmptyState
        icon={<HistoryIcon className="size-8 text-muted-foreground" />}
        title="No maintenance logged"
      />
    );
  return (
    <DataTable
      columns={columns(currency)}
      data={data?.items ?? []}
      serverPagination={{
        page,
        pageSize,
        totalCount: data?.totalCount ?? 0,
        onPageChange: setPage,
        onPageSizeChange: (size) => {
          setPageSize(size);
          setPage(1);
        },
        pending: query.isPlaceholderData,
        label: 'Maintenance logs',
      }}
    />
  );
}
