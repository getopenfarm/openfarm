import type { ColumnDef } from '@tanstack/react-table';
import { KeyIcon, PlusIcon } from 'lucide-react';
import { useNavigate } from 'react-router';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableColumnHeader } from '@/components/data-table/DataTableColumnHeader';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useOpenFarmApiEndpointsConnectionSecretsListConnectionSecretsEndpoint } from '@/lib/api/generated/connection-secrets/connection-secrets';
import type { ConnectionSecretResponse } from '@/lib/api/generated/endpoints.schemas';
import { formatDateTime } from '@/lib/formatDateTime';

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
        ? formatDateTime(row.original.createdAtUtc)
        : '',
  },
];

export function ConnectionSecretsPage() {
  const navigate = useNavigate();
  const listQuery =
    useOpenFarmApiEndpointsConnectionSecretsListConnectionSecretsEndpoint();

  const secrets = listQuery.data?.data ?? [];

  return (
    <Page>
      <PageHeader
        size="section"
        title="Connection credentials"
        description="Encrypted LAN access codes."
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
          description="Could not load connection credentials."
          onRetry={() => listQuery.refetch()}
        />
      ) : secrets.length === 0 ? (
        <EmptyState
          icon={<KeyIcon className="size-8 text-muted-foreground" />}
          title="No credentials"
          description="Add a LAN access code for a printer."
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
    </Page>
  );
}
