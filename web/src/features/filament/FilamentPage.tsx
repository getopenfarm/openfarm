import { useMemo } from 'react';
import { useNavigate } from 'react-router';
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
      <div className="flex items-center justify-between">
        <div>
          <h2 className="font-heading text-2xl font-semibold">Filament</h2>
          <p className="text-sm text-muted-foreground">
            Catalog · {filaments.length} materials · {variantCount} colour
            variants · {brandCount} brands
          </p>
        </div>
        <Button onClick={() => navigate('/filament/new')}>Add filament</Button>
      </div>
      <FilamentCatalogTable filaments={filaments} />
    </div>
  );
}
