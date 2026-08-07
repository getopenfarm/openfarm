import type { ColumnDef } from '@tanstack/react-table';
import { Layers3Icon, PlusIcon } from 'lucide-react';
import { useNavigate } from 'react-router';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useOpenFarmApiEndpointsAssembliesListAssembliesEndpoint } from '@/lib/api/generated/assemblies/assemblies';
import type {
  AssemblySummaryResponse,
  ListAssembliesResponse,
} from '@/lib/api/generated/endpoints.schemas';

const PAGE_SIZE = 500;

export function AssembliesPage() {
  const navigate = useNavigate();

  const assembliesQuery =
    useOpenFarmApiEndpointsAssembliesListAssembliesEndpoint({
      page: 1,
      pageSize: PAGE_SIZE,
    });
  const listData = assembliesQuery.data?.data as
    | ListAssembliesResponse
    | undefined;
  const assemblies = listData?.items ?? [];

  const columns: ColumnDef<AssemblySummaryResponse>[] = [
    {
      id: 'assembly',
      accessorKey: 'name',
      header: ({ column }) => (
        <DataTableColumnHeader column={column} title="Assembly" />
      ),
      cell: ({ row }) => (
        <div className="flex items-center gap-3">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
            <Layers3Icon className="size-5" />
          </div>
          <div className="flex flex-col">
            <span className="font-medium">{row.original.name}</span>
            {row.original.description && (
              <span className="text-xs text-muted-foreground">
                {row.original.description}
              </span>
            )}
          </div>
        </div>
      ),
    },
    {
      id: 'lineCount',
      header: 'Lines',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.lineCount} lines
        </span>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Assemblies"
        description="Group parts (and other assemblies) into reusable bills of materials."
        action={
          <Button onClick={() => navigate('/assemblies/new')}>
            <PlusIcon /> New assembly
          </Button>
        }
      />

      {assembliesQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : assemblies.length === 0 ? (
        <EmptyState
          icon={<Layers3Icon className="size-8 text-muted-foreground" />}
          title="No assemblies yet"
          description="Create one to group parts into a bill of materials."
          action={
            <Button onClick={() => navigate('/assemblies/new')}>
              <PlusIcon /> New assembly
            </Button>
          }
        />
      ) : (
        <DataTable
          columns={columns}
          data={assemblies}
          onRowClick={(row) => navigate(`/assemblies/${row.id}`)}
        />
      )}
    </div>
  );
}
