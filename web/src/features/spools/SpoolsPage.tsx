import { Package2Icon, PlusIcon } from 'lucide-react';
import { useNavigate } from 'react-router';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import type { ListSpoolsResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint } from '@/lib/api/generated/spools/spools';
import { pluralize } from '@/lib/pluralize';
import { SpoolsTable } from './components/SpoolsTable';

const LOW_REMAINING_FRACTION = 0.25;

export function SpoolsPage() {
  const navigate = useNavigate();
  const spoolsQuery = useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint();
  const spools =
    (spoolsQuery.data?.data as ListSpoolsResponse | undefined)?.spools ?? [];

  const inUseCount = spools.filter((spool) => spool.state === 'Using').length;
  const lowCount = spools.filter(
    (spool) =>
      spool.state === 'Using' &&
      (spool.remainingWeightGrams ?? 0) / (spool.spoolWeightGrams || 1) <
        LOW_REMAINING_FRACTION,
  ).length;

  return (
    <Page>
      <PageHeader
        title="Spools"
        description={
          <>
            Physical inventory · {pluralize(spools.length, 'spool')} ·{' '}
            <span className="text-kind-green font-medium">
              {inUseCount} in use
            </span>{' '}
            · <span className="text-kind-red font-medium">{lowCount} low</span>
          </>
        }
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
        <SpoolsTable spools={spools} />
      )}
    </Page>
  );
}
