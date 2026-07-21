import { Package2Icon } from 'lucide-react';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Skeleton } from '@/components/ui/skeleton';
import type { ListSpoolsResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsSpoolsListSpoolsEndpoint } from '@/lib/api/generated/spools/spools';
import { AddSpoolDialog } from './components/AddSpoolDialog';
import { SpoolsTable } from './components/SpoolsTable';

const LOW_REMAINING_FRACTION = 0.25;

export function SpoolsPage() {
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
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Spools"
        description={
          <>
            Physical inventory · {spools.length} spools ·{' '}
            <span className="text-kind-green font-medium">
              {inUseCount} in use
            </span>{' '}
            · <span className="text-kind-red font-medium">{lowCount} low</span>
          </>
        }
        action={<AddSpoolDialog />}
      />
      {spoolsQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : spools.length === 0 ? (
        <EmptyState
          icon={<Package2Icon className="size-8 text-muted-foreground" />}
          title="No spools yet"
          description="Add one to track it here."
          action={<AddSpoolDialog />}
        />
      ) : (
        <SpoolsTable spools={spools} />
      )}
    </div>
  );
}
