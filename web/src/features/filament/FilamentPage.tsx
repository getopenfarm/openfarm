import { PackageIcon, PlusIcon, StoreIcon } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router';
import {
  DataTableTabsList,
  DataTableToolbar,
} from '@/components/data-table/DataTableToolbar';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsTrigger } from '@/components/ui/tabs';
import type {
  ListFilamentsResponse,
  ListVendorsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint } from '@/lib/api/generated/filaments/filaments';
import { useOpenFarmApiEndpointsVendorsListVendorsEndpoint } from '@/lib/api/generated/vendors/vendors';
import { pluralize } from '@/lib/pluralize';
import { AddVendorDialog } from './components/AddVendorDialog';
import { FilamentCatalogTable } from './components/FilamentCatalogTable';
import { VendorTable } from './components/VendorTable';

export function FilamentPage() {
  const navigate = useNavigate();
  const [activeTab, setActiveTab] = useState('catalog');
  const filamentsQuery =
    useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint();
  const filaments =
    (filamentsQuery.data?.data as ListFilamentsResponse | undefined)
      ?.filaments ?? [];

  const vendorsQuery = useOpenFarmApiEndpointsVendorsListVendorsEndpoint();
  const vendors =
    (vendorsQuery.data?.data as ListVendorsResponse | undefined)?.vendors ?? [];

  const { vendorCount, variantCount } = useMemo(() => {
    const vendorNames = new Set(
      filaments.map((filament) => filament.vendorName),
    );
    const variants = filaments.reduce(
      (total, filament) => total + (filament.variants?.length ?? 0),
      0,
    );
    return { vendorCount: vendorNames.size, variantCount: variants };
  }, [filaments]);

  return (
    <Page>
      <PageHeader
        title="Filament"
        description={
          <>
            Catalog · {pluralize(filaments.length, 'material')} ·{' '}
            {pluralize(variantCount, 'color variant')} ·{' '}
            {pluralize(vendorCount, 'vendor')}
          </>
        }
        action={
          <Button onClick={() => navigate('/filament/new')}>
            <PlusIcon data-icon="inline-start" />
            Add filament
          </Button>
        }
      />
      <Tabs value={activeTab} onValueChange={setActiveTab} className="gap-4">
        <DataTableToolbar
          actions={
            activeTab === 'vendors' &&
            !vendorsQuery.isLoading &&
            vendors.length > 0 ? (
              <AddVendorDialog />
            ) : undefined
          }
        >
          <DataTableTabsList aria-label="Filament views">
            <TabsTrigger value="catalog">Catalog</TabsTrigger>
            <TabsTrigger value="vendors">Vendors</TabsTrigger>
          </DataTableTabsList>
        </DataTableToolbar>
        <TabsContent value="catalog" className="flex flex-col gap-3">
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
            <FilamentCatalogTable filaments={filaments} />
          )}
        </TabsContent>
        <TabsContent value="vendors" className="flex flex-col gap-3">
          {vendorsQuery.isLoading ? (
            <Skeleton className="h-64 w-full" />
          ) : vendors.length === 0 ? (
            <EmptyState
              icon={<StoreIcon className="size-8 text-muted-foreground" />}
              title="No vendors"
              description="Add a vendor to attribute filaments to a manufacturer."
              action={<AddVendorDialog />}
            />
          ) : (
            <VendorTable vendors={vendors} />
          )}
        </TabsContent>
      </Tabs>
    </Page>
  );
}
