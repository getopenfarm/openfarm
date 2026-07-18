import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { FilamentProfilesCard } from './components/FilamentProfilesCard';
import { VariantsCard } from './components/VariantsCard';
import type { Filament, FilamentVariant } from './data/mockFilamentStore';
import { filamentStore } from './data/mockFilamentStore';

const MATERIAL_TYPES: Filament['materialType'][] = [
  'PLA',
  'PETG',
  'ABS',
  'ASA',
  'TPU',
  'PC',
];

function emptyFilament(): Filament {
  return {
    id: `filament-${crypto.randomUUID()}`,
    brand: '',
    material: '',
    materialType: 'PLA',
    diameterMm: 1.75,
    profilesByNozzleSize: {},
    variants: [],
  };
}

interface FilamentFormPageProps {
  mode: 'add' | 'edit';
}

export function FilamentFormPage({ mode }: FilamentFormPageProps) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [filament, setFilament] = useState<Filament>(() => {
    if (mode === 'edit' && id) {
      const existing = filamentStore
        .getSnapshot()
        .find((entry) => entry.id === id);
      if (existing) {
        return existing;
      }
    }
    return emptyFilament();
  });

  const notFound =
    mode === 'edit' &&
    !filamentStore.getSnapshot().some((entry) => entry.id === id);

  function handleSave() {
    if (!filament.brand.trim() || !filament.material.trim()) {
      toast.error('Brand and material name are required.');
      return;
    }
    if (mode === 'add') {
      filamentStore.setState((prev) => [...prev, filament]);
      toast.success('Filament added.');
    } else {
      filamentStore.setState((prev) =>
        prev.map((entry) => (entry.id === filament.id ? filament : entry)),
      );
      toast.success('Filament saved.');
    }
    navigate('/filament');
  }

  function handleCancel() {
    navigate('/filament');
  }

  function handleAddVariant(variant: FilamentVariant) {
    setFilament((prev) => ({ ...prev, variants: [...prev.variants, variant] }));
  }

  function handleEditVariant(variant: FilamentVariant) {
    setFilament((prev) => ({
      ...prev,
      variants: prev.variants.map((entry) =>
        entry.id === variant.id ? variant : entry,
      ),
    }));
  }

  function handleRemoveVariant(variantId: string) {
    setFilament((prev) => ({
      ...prev,
      variants: prev.variants.filter((entry) => entry.id !== variantId),
    }));
  }

  if (notFound) {
    return (
      <div className="flex flex-col gap-6">
        <BackLink to="/filament">Back to filament</BackLink>
        <p className="text-sm text-muted-foreground">Filament not found.</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <BackLink to="/filament">Back to filament</BackLink>

      <PageHeader
        title={mode === 'add' ? 'Add filament' : 'Edit filament'}
        description={
          mode === 'add'
            ? 'Register a new material in the catalog with its colour variants.'
            : `Editing ${filament.brand} · ${filament.material}`
        }
      />

      <Card>
        <CardHeader>
          <CardTitle>Details</CardTitle>
        </CardHeader>
        <CardContent className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="brand">Brand</Label>
            <Input
              id="brand"
              value={filament.brand}
              onChange={(event) =>
                setFilament((prev) => ({ ...prev, brand: event.target.value }))
              }
              placeholder="e.g. Bambu Lab"
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="material">Material name</Label>
            <Input
              id="material"
              value={filament.material}
              onChange={(event) =>
                setFilament((prev) => ({
                  ...prev,
                  material: event.target.value,
                }))
              }
              placeholder="e.g. PLA Basic"
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="materialType">Material type</Label>
            <Select
              value={filament.materialType}
              onValueChange={(value) =>
                setFilament((prev) => ({
                  ...prev,
                  materialType: value as Filament['materialType'],
                }))
              }
            >
              <SelectTrigger id="materialType" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {MATERIAL_TYPES.map((type) => (
                  <SelectItem key={type} value={type}>
                    {type}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="diameter">Diameter (mm)</Label>
            <Input
              id="diameter"
              value={filament.diameterMm}
              onChange={(event) =>
                setFilament((prev) => ({
                  ...prev,
                  diameterMm: Number(event.target.value) || 0,
                }))
              }
              placeholder="1.75"
            />
          </div>
        </CardContent>
      </Card>

      <FilamentProfilesCard
        profilesByNozzleSize={filament.profilesByNozzleSize ?? {}}
        onChange={(nozzleSize, profileId) =>
          setFilament((prev) => ({
            ...prev,
            profilesByNozzleSize: {
              ...prev.profilesByNozzleSize,
              [nozzleSize]: profileId,
            },
          }))
        }
      />

      <VariantsCard
        variants={filament.variants}
        onAdd={handleAddVariant}
        onEdit={handleEditVariant}
        onRemove={handleRemoveVariant}
      />

      <FormFooter
        start={
          <Button type="button" variant="ghost" onClick={handleCancel}>
            Cancel
          </Button>
        }
        end={
          <Button type="button" onClick={handleSave}>
            {mode === 'add' ? 'Add filament' : 'Save changes'}
          </Button>
        }
      />
    </div>
  );
}
