import { useMemo } from 'react';
import { useNavigate } from 'react-router';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import type { ListFilamentsResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint } from '@/lib/api/generated/filaments/filaments';
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

  const { vendorCount, variantCount } = useMemo(() => {
    const vendors = new Set(filaments.map((filament) => filament.vendorName));
    const variants = filaments.reduce(
      (total, filament) => total + (filament.variants?.length ?? 0),
      0,
    );
    return { vendorCount: vendors.size, variantCount: variants };
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
          <FilamentCatalogTable filaments={filaments} />
        </TabsContent>
        <TabsContent value="vendors" className="flex flex-col gap-3">
          <div className="flex justify-end">
            <CreateVendorDialog />
          </div>
          <VendorTable />
        </TabsContent>
      </Tabs>
    </div>
  );
}
