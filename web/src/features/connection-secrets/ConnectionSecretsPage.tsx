import type { ColumnDef } from '@tanstack/react-table';
import { KeyIcon, PlusIcon } from 'lucide-react';
import { useNavigate } from 'react-router';
import { BackLink } from '@/components/back-link/BackLink';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useOpenFarmApiEndpointsConnectionSecretsListConnectionSecretsEndpoint } from '@/lib/api/generated/connection-secrets/connection-secrets';
import type { ConnectionSecretResponse } from '@/lib/api/generated/endpoints.schemas';

const columns: ColumnDef<ConnectionSecretResponse>[] = [
  {
    id: 'printerSerialNumber',
    accessorKey: 'printerSerialNumber',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Printer serial number" />
    ),
  },
  {
    id: 'maskedAccessCode',
    accessorKey: 'maskedAccessCode',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Access code" />
    ),
  },
  {
    id: 'createdAtUtc',
    header: ({ column }) => (
      <DataTableColumnHeader column={column} title="Created" />
    ),
    cell: ({ row }) =>
      row.original.createdAtUtc
        ? new Date(row.original.createdAtUtc).toLocaleString()
        : '',
  },
];

export function ConnectionSecretsPage() {
  const navigate = useNavigate();
  const listQuery =
    useOpenFarmApiEndpointsConnectionSecretsListConnectionSecretsEndpoint();

  const secrets = listQuery.data?.data ?? [];

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        above={
          <BackLink to="/settings" className="mb-1">
            Back to settings
          </BackLink>
        }
        title="Connection credentials"
        description="Encrypted LAN access codes used to connect directly to printers."
        action={
          <Button onClick={() => navigate('/settings/connection-secrets/new')}>
            <PlusIcon data-icon="inline-start" />
            Add credential
          </Button>
        }
      />

      {listQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : listQuery.isError ? (
        <ErrorState
          description="Couldn't load connection credentials."
          onRetry={() => listQuery.refetch()}
        />
      ) : secrets.length === 0 ? (
        <EmptyState
          icon={<KeyIcon className="size-8 text-muted-foreground" />}
          title="No credentials yet"
          description="Register a printer's LAN access code to connect to it directly."
          action={
            <Button
              onClick={() => navigate('/settings/connection-secrets/new')}
            >
              <PlusIcon data-icon="inline-start" />
              Add credential
            </Button>
          }
        />
      ) : (
        <DataTable pageSize={25} columns={columns} data={secrets} />
      )}
    </div>
  );
}
