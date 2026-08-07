import type { ColumnDef } from '@tanstack/react-table';
import { PackageIcon, PlusIcon } from 'lucide-react';
import { useNavigate } from 'react-router';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import type {
  ListNonPrintedComponentsResponse,
  NonPrintedComponentResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpoint } from '@/lib/api/generated/non-printed-components/non-printed-components';

const PAGE_SIZE = 500;

export function NonPrintedComponentsPage() {
  const navigate = useNavigate();

  const componentsQuery =
    useOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpoint(
      { page: 1, pageSize: PAGE_SIZE },
    );
  const listData = componentsQuery.data?.data as
    | ListNonPrintedComponentsResponse
    | undefined;
  const components = listData?.items ?? [];

  const columns: ColumnDef<NonPrintedComponentResponse>[] = [
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
          <span className="font-medium">{row.original.name}</span>
        </div>
      ),
    },
    {
      id: 'unit',
      header: 'Unit',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.unit}
        </span>
      ),
    },
    {
      id: 'unitCost',
      header: 'Unit cost',
      cell: ({ row }) => (
        <span className="font-mono text-sm">
          {row.original.unitCost != null
            ? row.original.unitCost.toFixed(2)
            : '—'}
        </span>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Non-printed components"
        description="Screws, inserts, packaging, and other reusable BOM components that aren't printed."
        action={
          <Button onClick={() => navigate('/non-printed-components/new')}>
            <PlusIcon /> New component
          </Button>
        }
      />

      {componentsQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : components.length === 0 ? (
        <EmptyState
          icon={<PackageIcon className="size-8 text-muted-foreground" />}
          title="No non-printed components yet"
          description="Create one to reference it from a BOM line."
          action={
            <Button onClick={() => navigate('/non-printed-components/new')}>
              <PlusIcon /> New component
            </Button>
          }
        />
      ) : (
        <DataTable
          columns={columns}
          data={components}
          onRowClick={(row) =>
            navigate(`/non-printed-components/${row.id}/edit`)
          }
        />
      )}
    </div>
  );
}
