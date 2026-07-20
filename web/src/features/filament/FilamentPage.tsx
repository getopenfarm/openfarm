import { PackageIcon, StoreIcon } from 'lucide-react';
import { useMemo } from 'react';
import { useNavigate } from 'react-router';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import type {
  ListFilamentsResponse,
  ListVendorsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint } from '@/lib/api/generated/filaments/filaments';
import { useOpenFarmApiEndpointsVendorsListVendorsEndpoint } from '@/lib/api/generated/vendors/vendors';
import { CreateVendorDialog } from './components/CreateVendorDialog';
import { FilamentCatalogTable } from './components/FilamentCatalogTable';
import { VendorTable } from './components/VendorTable';

export function FilamentPage() {
  const navigate = useNavigate();
  const filamentsQuery =
    useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint();
  const filaments =
    (filamentsQuery.data?.data as ListFilamentsResponse | undefined)
      ?.filaments ?? [];

  const vendorsQuery = useOpenFarmApiEndpointsVendorsListVendorsEndpoint();
  const vendors =
    (vendorsQuery.data?.data as ListVendorsResponse | undefined)?.vendors ??
    [];

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
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Filament"
        description={
          <>
            Catalog · {filaments.length} materials · {variantCount} colour
            variants · {vendorCount} vendors
          </>
        }
        action={
          <Button onClick={() => navigate('/filament/new')}>
            Add filament
          </Button>
        }
      />
      <Tabs defaultValue="catalog">
        <TabsList>
          <TabsTrigger value="catalog">Catalog</TabsTrigger>
          <TabsTrigger value="vendors">Vendors</TabsTrigger>
        </TabsList>
        <TabsContent value="catalog">
          {filamentsQuery.isLoading ? (
            <Skeleton className="h-64 w-full" />
          ) : filaments.length === 0 ? (
            <EmptyState
              icon={<PackageIcon className="size-8 text-muted-foreground" />}
              title="No filaments yet"
              description="Add a filament to start your catalog."
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
              title="No vendors yet"
              description="Add a vendor to attribute filaments to a manufacturer."
              action={<CreateVendorDialog />}
            />
          ) : (
            <>
              <div className="flex justify-end">
                <CreateVendorDialog />
              </div>
              <VendorTable vendors={vendors} />
            </>
          )}
        </TabsContent>
      </Tabs>
    </div>
  );
}
