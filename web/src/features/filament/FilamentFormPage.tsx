import { useQueryClient } from '@tanstack/react-query';
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
import { ProfilePickerCombobox } from '@/features/profiles/components/ProfilePickerCombobox';
import type {
  ListFilamentsResponse,
  ListVendorsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsFilamentsListFilamentsEndpointQueryKey,
  useOpenFarmApiEndpointsFilamentsAddFilamentVariantEndpoint,
  useOpenFarmApiEndpointsFilamentsCreateFilamentEndpoint,
  useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint,
} from '@/lib/api/generated/filaments/filaments';
import { useOpenFarmApiEndpointsVendorsListVendorsEndpoint } from '@/lib/api/generated/vendors/vendors';
import { CreateVendorDialog } from './components/CreateVendorDialog';
import type { NewFilamentVariant } from './components/VariantDialog';
import { VariantsCard } from './components/VariantsCard';

const MATERIAL_TYPES = ['PLA', 'PETG', 'ABS', 'ASA', 'TPU', 'PC'];

interface FilamentFormPageProps {
  mode: 'add' | 'edit';
}

export function FilamentFormPage({ mode }: FilamentFormPageProps) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const vendorsQuery = useOpenFarmApiEndpointsVendorsListVendorsEndpoint();
  const vendors =
    (vendorsQuery.data?.data as ListVendorsResponse | undefined)?.vendors ?? [];

  const filamentsQuery =
    useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint();
  const filaments =
    (filamentsQuery.data?.data as ListFilamentsResponse | undefined)
      ?.filaments ?? [];

  const [vendorId, setVendorId] = useState('');
  const [material, setMaterial] = useState('');
  const [materialType, setMaterialType] = useState(MATERIAL_TYPES[0]);
  const [filamentProfileId, setFilamentProfileId] = useState<string | null>(
    null,
  );
  const [variants, setVariants] = useState<NewFilamentVariant[]>([]);

  const createMutation = useOpenFarmApiEndpointsFilamentsCreateFilamentEndpoint(
    {
      mutation: {
        onSuccess: () => {
          toast.success('Filament added.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsFilamentsListFilamentsEndpointQueryKey(),
          });
          navigate('/filament');
        },
        onError: () => {
          toast.error('Could not add filament.');
        },
      },
    },
  );

  const addVariantMutation =
    useOpenFarmApiEndpointsFilamentsAddFilamentVariantEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Variant added.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsFilamentsListFilamentsEndpointQueryKey(),
          });
        },
        onError: () => {
          toast.error('Could not add variant.');
        },
      },
    });

  function handleCancel() {
    navigate('/filament');
  }

  function handleSave() {
    if (!vendorId || !material.trim()) {
      toast.error('Vendor and material name are required.');
      return;
    }
    if (variants.length === 0) {
      toast.error('Add at least one colour variant.');
      return;
    }
    createMutation.mutate({
      data: {
        vendorId,
        material: material.trim(),
        materialType,
        filamentProfileId,
        variants: variants.map((variant) => ({
          colorName: variant.colorName,
          colorHex: variant.colorHex,
        })),
      },
    });
  }

  if (mode === 'edit') {
    const existing = filaments.find((entry) => entry.id === id);

    if (filamentsQuery.isLoading) {
      return (
        <div className="flex flex-col gap-6">
          <BackLink to="/filament">Back to filament</BackLink>
        </div>
      );
    }

    if (!existing) {
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
          title="Filament variants"
          description={`${existing.vendorName} · ${existing.material} · ${existing.materialType}`}
        />

        <VariantsCard
          variants={existing.variants ?? []}
          isAdding={addVariantMutation.isPending}
          onAdd={(variant) => {
            if (!existing.id) return;
            addVariantMutation.mutate({
              id: existing.id,
              data: {
                colorName: variant.colorName,
                colorHex: variant.colorHex,
              },
            });
          }}
        />

        <FormFooter
          end={
            <Button type="button" onClick={() => navigate('/filament')}>
              Done
            </Button>
          }
        />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <BackLink to="/filament">Back to filament</BackLink>

      <PageHeader
        title="Add filament"
        description="Register a new material in the catalog with its colour variants."
      />

      <Card>
        <CardHeader>
          <CardTitle>Details</CardTitle>
        </CardHeader>
        <CardContent className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <div className="flex items-center justify-between">
              <Label htmlFor="vendor">Vendor</Label>
              <CreateVendorDialog
                onCreated={(vendor) => vendor.id && setVendorId(vendor.id)}
              />
            </div>
            <Select value={vendorId} onValueChange={setVendorId}>
              <SelectTrigger id="vendor" className="w-full">
                <SelectValue placeholder="Choose a vendor" />
              </SelectTrigger>
              <SelectContent>
                {vendors.map((vendor) => (
                  <SelectItem key={vendor.id} value={vendor.id ?? ''}>
                    {vendor.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="material">Material name</Label>
            <Input
              id="material"
              value={material}
              onChange={(event) => setMaterial(event.target.value)}
              placeholder="e.g. PLA Basic"
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="materialType">Material type</Label>
            <Select value={materialType} onValueChange={setMaterialType}>
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
            <span className="text-sm font-medium">
              Filament profile (optional)
            </span>
            <ProfilePickerCombobox
              kind="filament"
              value={filamentProfileId}
              onChange={(id) => setFilamentProfileId(id)}
              placeholder="No default profile"
              searchPlaceholder="Search filament profiles…"
              materialType={materialType}
            />
          </div>
        </CardContent>
      </Card>

      <VariantsCard
        variants={variants}
        onAdd={(variant) => setVariants((prev) => [...prev, variant])}
        onRemove={(index) =>
          setVariants((prev) => prev.filter((_, i) => i !== index))
        }
      />

      <FormFooter
        start={
          <Button type="button" variant="ghost" onClick={handleCancel}>
            Cancel
          </Button>
        }
        end={
          <Button
            type="button"
            onClick={handleSave}
            disabled={createMutation.isPending}
          >
            {createMutation.isPending ? 'Adding…' : 'Add filament'}
          </Button>
        }
      />
    </div>
  );
}
