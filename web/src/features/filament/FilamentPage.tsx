import { useMemo } from 'react';
import { useNavigate } from 'react-router';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { FilamentCatalogTable } from './components/FilamentCatalogTable';
import { useFilaments } from './data/mockFilamentStore';

export function FilamentPage() {
  const [filaments] = useFilaments();
  const navigate = useNavigate();

  const { brandCount, variantCount } = useMemo(() => {
    const brands = new Set(filaments.map((filament) => filament.brand));
    const variants = filaments.reduce(
      (total, filament) => total + filament.variants.length,
      0,
    );
    return { brandCount: brands.size, variantCount: variants };
  }, [filaments]);

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Filament"
        description={
          <>
            Catalog · {filaments.length} materials · {variantCount} colour
            variants · {brandCount} brands
          </>
        }
        action={
          <Button onClick={() => navigate('/filament/new')}>
            Add filament
          </Button>
        }
      />
      <FilamentCatalogTable filaments={filaments} />
    </div>
  );
}
