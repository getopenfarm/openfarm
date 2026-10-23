import { PackageIcon, PlusIcon } from 'lucide-react';
import { useMemo } from 'react';
import { useNavigate } from 'react-router';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import type {
  ListFilamentShortagesResponse,
  ListFilamentsResponse,
  ListVendorsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsFilamentsListFilamentShortagesEndpoint,
  useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint,
} from '@/lib/api/generated/filaments/filaments';
import { useOpenFarmApiEndpointsVendorsListVendorsEndpoint } from '@/lib/api/generated/vendors/vendors';
import { pluralize } from '@/lib/pluralize';
import { FilamentCatalogTable } from './components/FilamentCatalogTable';
import { StockTiles } from './components/StockTiles';

export function FilamentPage() {
  const navigate = useNavigate();
  const filamentsQuery =
    useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint();
  const list = filamentsQuery.data?.data as ListFilamentsResponse | undefined;
  const shortages = (
    useOpenFarmApiEndpointsFilamentsListFilamentShortagesEndpoint().data
      ?.data as ListFilamentShortagesResponse | undefined
  )?.shortages;
  const filaments = list?.filaments ?? [];

  const vendorsQuery = useOpenFarmApiEndpointsVendorsListVendorsEndpoint();
  const vendors =
    (vendorsQuery.data?.data as ListVendorsResponse | undefined)?.vendors ?? [];

  const variantCount = useMemo(
    () =>
      filaments.reduce(
        (total, filament) => total + (filament.variants?.length ?? 0),
        0,
      ),
    [filaments],
  );

  return (
    <Page>
      <PageHeader
        title="Filament"
        description={
          <>
            Catalog · {pluralize(filaments.length, 'material')} ·{' '}
            {pluralize(variantCount, 'color variant')} ·{' '}
            {pluralize(vendors.length, 'vendor')}
          </>
        }
        action={
          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="outline"
              onClick={() => navigate('/filament/spools/new')}
            >
              <PlusIcon data-icon="inline-start" />
              Add spools
            </Button>
            <Button onClick={() => navigate('/filament/new')}>
              <PlusIcon data-icon="inline-start" />
              Add filament
            </Button>
          </div>
        }
      />
      {filamentsQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : filaments.length === 0 ? (
        <EmptyState
          icon={<PackageIcon className="size-8 text-muted-foreground" />}
          title="No filaments"
          description="Add a filament to start a catalog."
          action={
            <Button onClick={() => navigate('/filament/new')}>
              Add filament
            </Button>
          }
        />
      ) : (
        <div className="flex flex-col gap-4">
          <StockTiles stock={list?.stock ?? []} />
          <FilamentCatalogTable
            filaments={filaments}
            stock={list?.stock ?? []}
            shortages={shortages ?? []}
          />
        </div>
      )}
    </Page>
  );
}
