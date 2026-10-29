import type { ColumnDef } from '@tanstack/react-table';
import { PlusIcon } from 'lucide-react';
import { useRef } from 'react';
import { Link } from 'react-router';
import { DataTable } from '@/components/data-table/DataTable';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import {
  DropdownMenuGroup,
  DropdownMenuItem,
} from '@/components/ui/dropdown-menu';
import { Skeleton } from '@/components/ui/skeleton';
import { feeTotals } from '@/features/assemblies/pricing';
import type { FeeProfileDto } from '@/lib/api/generated/endpoints.schemas';
import { pluralize } from '@/lib/pluralize';
import { useFeeProfileRemoval } from './FeeProfileRemoval';
import { feeSummary, useFeeProfiles } from './fees';
import { useCurrency } from './generalSettings';

export function FeesPage() {
  const query = useFeeProfiles();
  const currency = useCurrency();
  const removal = useFeeProfileRemoval();
  const activeRowId = useRef<string | null | undefined>(null);
  const columns: ColumnDef<FeeProfileDto>[] = [
    {
      accessorKey: 'name',
      header: 'Fee profile',
      cell: ({ row }) => (
        <Link to={`/settings/fees/${row.original.id}`}>
          {row.original.name}
        </Link>
      ),
    },
    {
      id: 'fees',
      header: 'Fees',
      cell: ({ row }) => {
        const count = row.original.fees?.length ?? 0;
        return count ? pluralize(count, 'fee') : 'No fees';
      },
    },
    {
      id: 'total',
      header: 'Combined fees',
      cell: ({ row }) => {
        const total = feeTotals(row.original.fees ?? []);
        return feeSummary(total.feePercent, total.fixedFee, currency);
      },
    },
    {
      id: 'actions',
      header: '',
      cell: ({ row }) => (
        <DataTableRowActions
          label={row.original.name ?? 'fee profile'}
          disabled={removal.pending}
          triggerRef={(node) => {
            if (activeRowId.current === row.original.id)
              removal.returnFocusRef.current = node;
          }}
          onOpen={(trigger) => {
            activeRowId.current = row.original.id;
            removal.returnFocusRef.current = trigger;
          }}
        >
          <DropdownMenuGroup>
            <DropdownMenuItem asChild>
              <Link to={`/settings/fees/${row.original.id}`}>Edit</Link>
            </DropdownMenuItem>
            <DropdownMenuItem
              variant="destructive"
              onSelect={() => removal.open(row.original)}
            >
              Remove
            </DropdownMenuItem>
          </DropdownMenuGroup>
        </DataTableRowActions>
      ),
    },
  ];
  const add = (
    <Button asChild>
      <Link ref={removal.fallbackFocusRef} to="/settings/fees/new">
        <PlusIcon data-icon="inline-start" />
        Add fee profile
      </Link>
    </Button>
  );
  return (
    <Page>
      <PageHeader
        title="Fees"
        size="section"
        description="Manage fee profiles used to estimate assembly selling prices and profit."
        action={add}
      />
      {query.data ? (
        <DataTable
          columns={columns}
          data={query.data.data.profiles ?? []}
          getRowId={(row) => row.id ?? ''}
          pageSize={25}
          emptyState={
            <EmptyState
              title="No fee profiles"
              description="Group the fees charged on each sale into a profile, such as Shopee or Direct. A profile can also have no fees."
              action={
                <Button asChild>
                  <Link to="/settings/fees/new">Add fee profile</Link>
                </Button>
              }
              bordered={false}
            />
          }
        />
      ) : query.isError ? (
        <ErrorState
          title="Could not load fees"
          onRetry={() => void query.refetch()}
        />
      ) : (
        <Skeleton className="h-40" />
      )}
      {removal.dialog}
    </Page>
  );
}
