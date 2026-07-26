import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
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
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
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
import { VariantsCard } from './components/VariantsCard';

const MATERIAL_TYPES = ['PLA', 'PETG', 'ABS', 'ASA', 'TPU', 'PC'] as const;

const schema = z.object({
  vendorId: z.string().min(1, 'Vendor is required'),
  material: z.string().min(1, 'Material name is required'),
  materialType: z.enum(MATERIAL_TYPES),
  filamentProfileId: z.string().nullable(),
  variants: z
    .array(z.object({ colorName: z.string(), colorHex: z.string() }))
    .min(1, 'Add at least one colour variant'),
});

type FormValues = z.infer<typeof schema>;

interface FilamentFormPageProps {
  mode: 'add' | 'edit';
}

export function FilamentFormPage({ mode }: FilamentFormPageProps) {
  if (mode === 'edit') {
    return <FilamentVariantsPage />;
  }
  return <AddFilamentForm />;
}

function AddFilamentForm() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const vendorsQuery = useOpenFarmApiEndpointsVendorsListVendorsEndpoint();
  const vendors =
    (vendorsQuery.data?.data as ListVendorsResponse | undefined)?.vendors ?? [];

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      vendorId: '',
      material: '',
      materialType: MATERIAL_TYPES[0],
      filamentProfileId: null,
      variants: [],
    },
  });

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );

  const createMutation = useOpenFarmApiEndpointsFilamentsCreateFilamentEndpoint(
    {
      mutation: {
        onSuccess: () => {
          toast.success('Filament added.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsFilamentsListFilamentsEndpointQueryKey(),
          });
          markClean();
          navigate('/filament');
        },
        onError: () => {
          toast.error('Could not add filament.');
        },
      },
    },
  );

  const materialType = form.watch('materialType');
  const variants = form.watch('variants');

  function onSubmit(values: FormValues) {
    createMutation.mutate({
      data: {
        vendorId: values.vendorId,
        material: values.material.trim(),
        materialType: values.materialType,
        filamentProfileId: values.filamentProfileId,
        variants: values.variants.map((variant) => ({
          colorName: variant.colorName,
          colorHex: variant.colorHex,
        })),
      },
    });
  }

  return (
    <div className="flex flex-col gap-6">
      <BackLink to="/filament">Back to filament</BackLink>

      <PageHeader
        title="Add filament"
        description="Register a new material in the catalog with its colour variants."
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-col gap-6"
        >
          <Card>
            <CardHeader>
              <CardTitle>Details</CardTitle>
            </CardHeader>
            <CardContent className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <FormField
                control={form.control}
                name="vendorId"
                render={({ field }) => (
                  <FormItem>
                    <div className="flex items-center justify-between">
                      <FormLabel>Vendor</FormLabel>
                      <CreateVendorDialog
                        onCreated={(vendor) =>
                          vendor.id &&
                          form.setValue('vendorId', vendor.id, {
                            shouldValidate: true,
                            shouldDirty: true,
                          })
                        }
                      />
                    </div>
                    <Select value={field.value} onValueChange={field.onChange}>
                      <FormControl>
                        <SelectTrigger className="w-full">
                          <SelectValue placeholder="Choose a vendor" />
                        </SelectTrigger>
                      </FormControl>
                      <SelectContent>
                        {vendors.map((vendor) => (
                          <SelectItem key={vendor.id} value={vendor.id ?? ''}>
                            {vendor.name}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="material"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Material name</FormLabel>
                    <FormControl>
                      <Input placeholder="e.g. PLA Basic" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="materialType"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Material type</FormLabel>
                    <Select value={field.value} onValueChange={field.onChange}>
                      <FormControl>
                        <SelectTrigger className="w-full">
                          <SelectValue />
                        </SelectTrigger>
                      </FormControl>
                      <SelectContent>
                        {MATERIAL_TYPES.map((type) => (
                          <SelectItem key={type} value={type}>
                            {type}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <div className="flex flex-col gap-1.5">
                <Label>Filament profile (optional)</Label>
                <ProfilePickerCombobox
                  kind="filament"
                  value={form.watch('filamentProfileId')}
                  onChange={(id) => form.setValue('filamentProfileId', id)}
                  placeholder="No default profile"
                  searchPlaceholder="Search filament profiles…"
                  materialType={materialType}
                />
              </div>
            </CardContent>
          </Card>

          <FormField
            control={form.control}
            name="variants"
            render={() => (
              <FormItem>
                <FormControl>
                  <VariantsCard
                    variants={variants}
                    onAdd={(variant) =>
                      form.setValue('variants', [...variants, variant], {
                        shouldValidate: true,
                        shouldDirty: true,
                      })
                    }
                    onRemove={(index) =>
                      form.setValue(
                        'variants',
                        variants.filter((_, i) => i !== index),
                        { shouldValidate: true, shouldDirty: true },
                      )
                    }
                  />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />

          <FormFooter
            start={
              <Button
                type="button"
                variant="ghost"
                onClick={() => navigate('/filament')}
              >
                Cancel
              </Button>
            }
            end={
              <Button type="submit" disabled={createMutation.isPending}>
                {createMutation.isPending ? 'Adding…' : 'Add filament'}
              </Button>
            }
          />
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </div>
  );
}

function FilamentVariantsPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const filamentsQuery =
    useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint();
  const filaments =
    (filamentsQuery.data?.data as ListFilamentsResponse | undefined)
      ?.filaments ?? [];

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
