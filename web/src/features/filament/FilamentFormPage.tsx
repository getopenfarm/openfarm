import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { type ReactNode, useState } from 'react';
import { type UseFormReturn, useForm } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { FieldGroup } from '@/components/ui/field';
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { invalidateSlicingChecks } from '@/features/slicing/invalidateSlicingChecks';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type {
  FilamentProfileAssignmentInput,
  FilamentProfileAssignmentIssue,
  FilamentProfileAssignmentsResponse,
  FilamentResponse,
  ListFilamentsResponse,
  VendorResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsFilamentsGetFilamentProfileAssignmentsEndpointQueryKey,
  getOpenFarmApiEndpointsFilamentsListFilamentsEndpointQueryKey,
  useOpenFarmApiEndpointsFilamentsCreateFilamentEndpoint,
  useOpenFarmApiEndpointsFilamentsEditFilamentEndpoint,
  useOpenFarmApiEndpointsFilamentsGetFilamentProfileAssignmentsEndpoint,
  useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint,
} from '@/lib/api/generated/filaments/filaments';
import {
  getOpenFarmApiEndpointsVendorsListVendorsEndpointQueryKey,
  useOpenFarmApiEndpointsVendorsCreateVendorEndpoint,
} from '@/lib/api/generated/vendors/vendors';
import { ProfileAssignmentsCard } from './components/ProfileAssignmentsCard';
import type { VariantDialogResult } from './components/VariantDialog';
import { VariantsCard } from './components/VariantsCard';
import { VendorCombobox } from './components/VendorCombobox';
import { inferMaterialType, MATERIAL_TYPES } from './lib/materialType';

const priceSchema = z
  .number()
  .finite()
  .nonnegative('Price must be non-negative')
  .nullable();

const schema = z.object({
  pricePerKg: priceSchema,
  // Nested so its refine runs even when sibling fields fail.
  vendor: z
    .object({ vendorId: z.string(), newVendorName: z.string().optional() })
    .refine((v) => v.vendorId || v.newVendorName, 'Vendor is required'),
  material: z.string().min(1, 'Name is required'),
  materialType: z.enum(MATERIAL_TYPES),
  profileAssignments: z.array(
    z.object({
      brand: z.string(),
      modelCode: z.string(),
      nozzleDiameterMm: z.number().positive(),
      filamentProfileId: z.string(),
      colorHex: z.string().nullable().optional(),
    }),
  ),
  variants: z
    .array(
      z.object({
        id: z.string().optional(),
        pricePerKg: priceSchema,
        colorName: z.string(),
        colorHex: z.string(),
      }),
    )
    .min(1, 'Add at least one variant'),
});

type FormValues = z.infer<typeof schema>;

function toAssignmentValues(
  assignments: FilamentProfileAssignmentInput[],
): FormValues['profileAssignments'] {
  return assignments.map((a) => ({
    brand: a.brand ?? '',
    modelCode: a.modelCode ?? '',
    nozzleDiameterMm: a.nozzleDiameterMm ?? 0,
    filamentProfileId: a.filamentProfileId ?? '',
    colorHex: a.colorHex,
  }));
}

function toEditValues(
  filament: FilamentResponse,
  assignments: FilamentProfileAssignmentInput[],
): FormValues {
  return {
    vendor: { vendorId: filament.vendorId ?? '' },
    material: filament.material ?? '',
    pricePerKg: filament.pricePerKg ?? null,
    materialType:
      MATERIAL_TYPES.find((type) => type === filament.materialType) ??
      MATERIAL_TYPES[0],
    profileAssignments: toAssignmentValues(assignments),
    variants: (filament.variants ?? []).map((variant) => ({
      id: variant.id,
      pricePerKg: variant.pricePerKg ?? null,
      colorName: variant.colorName ?? '',
      colorHex: variant.colorHex ?? '',
    })),
  };
}

/** Creates the typed new vendor (if any) right before the filament save; returns its id. */
function useResolveVendorId() {
  const queryClient = useQueryClient();
  const createVendor = useOpenFarmApiEndpointsVendorsCreateVendorEndpoint();

  async function resolve(vendor: FormValues['vendor']): Promise<string | null> {
    if (!vendor.newVendorName) return vendor.vendorId;
    try {
      const response = await createVendor.mutateAsync({
        data: { name: vendor.newVendorName },
      });
      await queryClient.invalidateQueries({
        queryKey: getOpenFarmApiEndpointsVendorsListVendorsEndpointQueryKey(),
      });
      return (response.data as VendorResponse).id ?? null;
    } catch (error) {
      toast.error(await extractErrorDetail(error, 'Could not add vendor.'));
      return null;
    }
  }

  return { resolve, isPending: createVendor.isPending };
}

/** Vendor / Name / Type — shared by add and edit. A type change drops profile assignments,
 * which the server validates against the material type.
 *
 * With `inferType` (add only), the name suggests the type ("PETG Basic" → PETG) until the user
 * picks one. It never overrides a manual pick, and it stops once profiles are assigned, since a
 * type change would drop them. */
function MaterialFields({
  form,
  inferType = false,
}: {
  form: UseFormReturn<FormValues>;
  inferType?: boolean;
}) {
  const [typePicked, setTypePicked] = useState(false);
  const [typeInferred, setTypeInferred] = useState(false);

  function suggestType(name: string) {
    if (
      !inferType ||
      typePicked ||
      form.getValues('profileAssignments').length > 0
    ) {
      return;
    }
    const type = inferMaterialType(name);
    setTypeInferred(type != null);
    if (type) form.setValue('materialType', type, { shouldDirty: true });
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Material</CardTitle>
      </CardHeader>
      <CardContent className="grid grid-cols-1 items-start gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <FormField
          control={form.control}
          name="vendor"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Vendor</FormLabel>
              <FormControl>
                <VendorCombobox
                  value={field.value}
                  onChange={(next) =>
                    form.setValue('vendor', next, {
                      shouldDirty: true,
                      shouldValidate: form.formState.isSubmitted,
                    })
                  }
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={form.control}
          name="material"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Name</FormLabel>
              <FormControl>
                <Input
                  placeholder="e.g. PLA Basic"
                  {...field}
                  onChange={(event) => {
                    field.onChange(event);
                    suggestType(event.target.value);
                  }}
                />
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
              <FormLabel>Type</FormLabel>
              <Select
                value={field.value}
                onValueChange={(value) => {
                  field.onChange(value);
                  setTypePicked(true);
                  setTypeInferred(false);
                  form.setValue('profileAssignments', [], {
                    shouldDirty: true,
                  });
                }}
              >
                <FormControl>
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                </FormControl>
                <SelectContent>
                  <SelectGroup>
                    {MATERIAL_TYPES.map((type) => (
                      <SelectItem key={type} value={type}>
                        {type}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
              {typeInferred && (
                <FormDescription>Detected from name</FormDescription>
              )}
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={form.control}
          name="pricePerKg"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Price per kg</FormLabel>
              <FormControl>
                <Input
                  type="number"
                  min="0"
                  step="any"
                  value={field.value ?? ''}
                  onChange={(event) =>
                    field.onChange(
                      event.target.value === ''
                        ? null
                        : Number(event.target.value),
                    )
                  }
                  onBlur={field.onBlur}
                  name={field.name}
                  ref={field.ref}
                />
              </FormControl>
              <FormDescription>
                Default for every variant. Blank if unknown.
              </FormDescription>
              <FormMessage />
            </FormItem>
          )}
        />
      </CardContent>
    </Card>
  );
}

/** Writes a variant from the variant dialog into the draft, along with its color's profile
 * overrides. Overrides are keyed by color, so they follow a staged variant's color change. */
function applyVariant(
  form: UseFormReturn<FormValues>,
  index: number | null,
  { variant, overrides }: VariantDialogResult,
) {
  const variants = form.getValues('variants');
  const previousColor = index == null ? null : variants[index]?.colorHex;
  form.setValue(
    'profileAssignments',
    [
      ...form
        .getValues('profileAssignments')
        .filter((a) => !a.colorHex || a.colorHex !== previousColor),
      ...toAssignmentValues(overrides),
    ],
    { shouldDirty: true },
  );
  form.setValue(
    'variants',
    index == null
      ? [...variants, variant]
      : variants.map((existing, i) => (i === index ? variant : existing)),
    { shouldDirty: true, shouldValidate: form.formState.isSubmitted },
  );
}

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

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      vendor: { vendorId: '' },
      material: '',
      pricePerKg: null,
      materialType: MATERIAL_TYPES[0],
      profileAssignments: [],
      variants: [],
    },
  });

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );

  const vendorResolver = useResolveVendorId();
  const createMutation = useOpenFarmApiEndpointsFilamentsCreateFilamentEndpoint(
    {
      mutation: {
        onSuccess: () => {
          toast.success('Filament added.');
          invalidateSlicingChecks(queryClient);
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsFilamentsListFilamentsEndpointQueryKey(),
          });
          markClean();
          navigate('/filament');
        },
        onError: async (error) => {
          toast.error(
            await extractErrorDetail(error, 'Could not add filament.'),
          );
        },
      },
    },
  );

  const isPending = vendorResolver.isPending || createMutation.isPending;
  const materialType = form.watch('materialType');
  const variants = form.watch('variants');
  const pricePerKg = form.watch('pricePerKg');
  const profileAssignments = form.watch('profileAssignments');

  async function onSubmit(values: FormValues) {
    const vendorId = await vendorResolver.resolve(values.vendor);
    if (!vendorId) return;
    createMutation.mutate({
      data: {
        vendorId,
        material: values.material.trim(),
        pricePerKg: values.pricePerKg,
        materialType: values.materialType,
        profileAssignments: values.profileAssignments,
        variants: values.variants.map((variant) => ({
          pricePerKg: variant.pricePerKg,
          colorName: variant.colorName,
          colorHex: variant.colorHex,
        })),
      },
    });
  }

  return (
    <Page className="flex-1">
      <PageHeader
        above={
          <BackLink to="/filament" className="mb-1">
            Back to filament
          </BackLink>
        }
        title="Add filament"
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="flex flex-1 flex-col gap-6"
        >
          <FieldGroup className="flex-1 gap-6">
            <MaterialFields form={form} inferType />

            <FormField
              control={form.control}
              name="variants"
              render={() => (
                <FormItem>
                  <FormControl>
                    <VariantsCard
                      variants={variants}
                      inheritedPrice={pricePerKg}
                      materialType={materialType}
                      assignments={profileAssignments}
                      onSave={(index, result) =>
                        applyVariant(form, index, result)
                      }
                      onRemove={(index) => {
                        const removedColor = variants[index]?.colorHex;
                        form.setValue(
                          'profileAssignments',
                          form
                            .getValues('profileAssignments')
                            .filter((a) => a.colorHex !== removedColor),
                          { shouldDirty: true },
                        );
                        form.setValue(
                          'variants',
                          variants.filter((_, i) => i !== index),
                          { shouldValidate: true, shouldDirty: true },
                        );
                      }}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <ProfileAssignmentsCard
              materialType={materialType}
              variants={variants}
              assignments={profileAssignments}
              onChange={(value) =>
                form.setValue('profileAssignments', toAssignmentValues(value), {
                  shouldDirty: true,
                })
              }
            />

            <FormFooter
              end={
                <>
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => navigate('/filament')}
                  >
                    Cancel
                  </Button>
                  <Button type="submit" disabled={isPending}>
                    {isPending && <Spinner data-icon="inline-start" />}
                    {isPending ? 'Adding…' : 'Add filament'}
                  </Button>
                </>
              }
            />
          </FieldGroup>
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </Page>
  );
}

function FilamentVariantsPage() {
  const { id = '' } = useParams<{ id: string }>();

  const filamentsQuery =
    useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint();
  const assignmentsQuery =
    useOpenFarmApiEndpointsFilamentsGetFilamentProfileAssignmentsEndpoint(id);
  const filaments =
    (filamentsQuery.data?.data as ListFilamentsResponse | undefined)
      ?.filaments ?? [];
  const existing = filaments.find((entry) => entry.id === id);
  const assignments = assignmentsQuery.data?.data as
    | FilamentProfileAssignmentsResponse
    | undefined;

  const backLink = (
    <BackLink to="/filament" className="mb-1">
      Back to filament
    </BackLink>
  );

  if (filamentsQuery.isLoading || assignmentsQuery.isLoading) {
    return (
      <Page>
        <PageHeader above={backLink} title="Filament" />
        <Skeleton className="h-40 w-full" />
        <Skeleton className="h-64 w-full" />
      </Page>
    );
  }

  if (filamentsQuery.isError || assignmentsQuery.isError) {
    return (
      <Page>
        <PageHeader above={backLink} title="Filament" />
        <ErrorState
          title="Could not load filament"
          onRetry={() => {
            filamentsQuery.refetch();
            assignmentsQuery.refetch();
          }}
        />
      </Page>
    );
  }

  if (!existing) {
    return (
      <Page>
        <PageHeader above={backLink} title="Filament" />
        <EmptyState
          title="Filament not found"
          description="Removed or invalid link."
          action={
            <Button asChild variant="outline">
              <Link to="/filament">Back to filament</Link>
            </Button>
          }
        />
      </Page>
    );
  }

  return (
    <EditFilamentForm
      filament={existing}
      assignments={assignments?.assignments ?? []}
      issues={assignments?.issues}
      backLink={backLink}
    />
  );
}

function EditFilamentForm({
  filament,
  assignments,
  issues,
  backLink,
}: {
  filament: FilamentResponse;
  assignments: FilamentProfileAssignmentInput[];
  issues?: FilamentProfileAssignmentIssue[];
  backLink: ReactNode;
}) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const filamentId = filament.id ?? '';

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: toEditValues(filament, assignments),
  });
  const { isDirty } = form.formState;
  const { ConfirmLeaveDialog } = useUnsavedChangesPrompt(isDirty);

  const vendorResolver = useResolveVendorId();
  const editMutation = useOpenFarmApiEndpointsFilamentsEditFilamentEndpoint({
    mutation: {
      onSuccess: (response) => {
        const saved = response.data as FilamentResponse;
        form.reset(toEditValues(saved, saved.profileAssignments ?? []));
        queryClient.setQueryData(
          getOpenFarmApiEndpointsFilamentsListFilamentsEndpointQueryKey(),
          (old: { data: ListFilamentsResponse } | undefined) =>
            old && {
              ...old,
              data: {
                ...old.data,
                filaments: old.data.filaments?.map((entry) =>
                  entry.id === saved.id ? saved : entry,
                ),
              },
            },
        );
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsFilamentsListFilamentsEndpointQueryKey(),
        });
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsFilamentsGetFilamentProfileAssignmentsEndpointQueryKey(
              filamentId,
            ),
        });
        invalidateSlicingChecks(queryClient);
        toast.success('Filament saved.');
      },
      onError: async (error: unknown) => {
        toast.error(
          await extractErrorDetail(error, 'Could not save filament.'),
        );
      },
    },
  });

  const isPending = vendorResolver.isPending || editMutation.isPending;
  const materialType = form.watch('materialType');
  const variants = form.watch('variants');
  const pricePerKg = form.watch('pricePerKg');
  const profileAssignments = form.watch('profileAssignments');

  async function onSubmit(values: FormValues) {
    const vendorId = await vendorResolver.resolve(values.vendor);
    if (!vendorId) return;
    editMutation.mutate({
      id: filamentId,
      data: {
        vendorId,
        material: values.material.trim(),
        pricePerKg: values.pricePerKg,
        materialType: values.materialType,
        newVariants: values.variants
          .filter((variant) => !variant.id)
          .map((variant) => ({
            pricePerKg: variant.pricePerKg,
            colorName: variant.colorName,
            colorHex: variant.colorHex,
          })),
        variantPrices: values.variants
          .filter((variant): variant is typeof variant & { id: string } =>
            Boolean(variant.id),
          )
          .map((variant) => ({
            variantId: variant.id,
            pricePerKg: variant.pricePerKg,
          })),
        // Full replace: always send the whole current list, or the server wipes assignments.
        profileAssignments: values.profileAssignments,
      },
    });
  }

  return (
    <Page className="flex-1">
      <PageHeader
        above={backLink}
        title={filament.material}
        description={`${filament.vendorName} · ${filament.materialType}`}
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="flex flex-1 flex-col gap-6"
        >
          <FieldGroup className="flex-1 gap-6">
            <MaterialFields form={form} />

            <VariantsCard
              variants={variants}
              inheritedPrice={pricePerKg}
              materialType={materialType}
              assignments={profileAssignments}
              issues={issues}
              onSave={(index, result) => applyVariant(form, index, result)}
              onRemove={(index) => {
                const removedColor = variants[index]?.colorHex;
                form.setValue(
                  'profileAssignments',
                  form
                    .getValues('profileAssignments')
                    .filter((a) => a.colorHex !== removedColor),
                  { shouldDirty: true },
                );
                form.setValue(
                  'variants',
                  variants.filter((_, i) => i !== index),
                  { shouldDirty: true },
                );
              }}
            />

            <ProfileAssignmentsCard
              materialType={materialType}
              variants={variants}
              assignments={profileAssignments}
              issues={issues}
              onChange={(value) =>
                form.setValue('profileAssignments', toAssignmentValues(value), {
                  shouldDirty: true,
                })
              }
            />

            <FormFooter
              end={
                <>
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => navigate('/filament')}
                  >
                    Cancel
                  </Button>
                  <Button type="submit" disabled={!isDirty || isPending}>
                    {isPending && <Spinner data-icon="inline-start" />}
                    {isPending ? 'Saving…' : 'Save'}
                  </Button>
                </>
              }
            />
          </FieldGroup>
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </Page>
  );
}
