import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { useForm } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { FieldGroup } from '@/components/ui/field';
import {
  Form,
  FormControl,
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
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type {
  FilamentProfileAssignmentInput,
  FilamentProfileAssignmentIssue,
  FilamentProfileAssignmentsResponse,
  FilamentResponse,
  ListFilamentsResponse,
  ListVendorsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsFilamentsGetFilamentProfileAssignmentsEndpointQueryKey,
  getOpenFarmApiEndpointsFilamentsListFilamentsEndpointQueryKey,
  useOpenFarmApiEndpointsFilamentsCreateFilamentEndpoint,
  useOpenFarmApiEndpointsFilamentsEditFilamentEndpoint,
  useOpenFarmApiEndpointsFilamentsGetFilamentProfileAssignmentsEndpoint,
  useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint,
} from '@/lib/api/generated/filaments/filaments';
import { useOpenFarmApiEndpointsVendorsListVendorsEndpoint } from '@/lib/api/generated/vendors/vendors';
import { CreateVendorDialog } from './components/CreateVendorDialog';
import { ProfileAssignmentsCard } from './components/ProfileAssignmentsCard';
import { type DisplayedVariant, VariantsCard } from './components/VariantsCard';

const MATERIAL_TYPES = ['PLA', 'PETG', 'ABS', 'ASA', 'TPU', 'PC'] as const;

const schema = z.object({
  vendorId: z.string().min(1, 'Vendor is required'),
  material: z.string().min(1, 'Material name is required'),
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
      profileAssignments: [],
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
        profileAssignments: values.profileAssignments,
        variants: values.variants.map((variant) => ({
          colorName: variant.colorName,
          colorHex: variant.colorHex,
        })),
      },
    });
  }

  return (
    <div className="flex flex-1 flex-col gap-6">
      <PageHeader
        above={
          <BackLink to="/filament" className="mb-1">
            Back to filament
          </BackLink>
        }
        title="Add filament"
        description="Register a new material in the catalog with its colour variants."
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-1 flex-col gap-6"
        >
          <FieldGroup className="flex-1 gap-6">
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
                      <Select
                        value={field.value}
                        onValueChange={field.onChange}
                      >
                        <FormControl>
                          <SelectTrigger className="w-full">
                            <SelectValue placeholder="Choose a vendor" />
                          </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                          <SelectGroup>
                            {vendors.map((vendor) => (
                              <SelectItem
                                key={vendor.id}
                                value={vendor.id ?? ''}
                              >
                                {vendor.name}
                              </SelectItem>
                            ))}
                          </SelectGroup>
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
                      <Select
                        value={field.value}
                        onValueChange={field.onChange}
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
                      <FormMessage />
                    </FormItem>
                  )}
                />
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
              assignments={form.watch('profileAssignments')}
              onChange={(value) =>
                form.setValue(
                  'profileAssignments',
                  value.map((a) => ({
                    brand: a.brand ?? '',
                    modelCode: a.modelCode ?? '',
                    nozzleDiameterMm: a.nozzleDiameterMm ?? 0,
                    filamentProfileId: a.filamentProfileId ?? '',
                    colorHex: a.colorHex,
                  })),
                  { shouldDirty: true },
                )
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
                  <Button type="submit" disabled={createMutation.isPending}>
                    {createMutation.isPending && (
                      <Spinner data-icon="inline-start" />
                    )}
                    {createMutation.isPending ? 'Saving…' : 'Save'}
                  </Button>
                </>
              }
            />
          </FieldGroup>
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </div>
  );
}

interface EditDraft {
  variants: DisplayedVariant[];
  profileAssignments: FilamentProfileAssignmentInput[];
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
      <div className="flex flex-col gap-6">
        <PageHeader above={backLink} title="Edit filament" />
        <Skeleton className="h-40 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (filamentsQuery.isError || assignmentsQuery.isError) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader above={backLink} title="Edit filament" />
        <ErrorState
          title="Could not load filament"
          onRetry={() => {
            filamentsQuery.refetch();
            assignmentsQuery.refetch();
          }}
        />
      </div>
    );
  }

  if (!existing) {
    return (
      <div className="flex flex-col gap-6">
        <PageHeader above={backLink} title="Edit filament" />
        <EmptyState
          title="Filament not found"
          description="It may have been deleted."
          action={
            <Button asChild variant="outline">
              <Link to="/filament">Back to filament</Link>
            </Button>
          }
        />
      </div>
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

  const form = useForm<EditDraft>({
    defaultValues: {
      variants: filament.variants ?? [],
      profileAssignments: assignments,
    },
  });
  const { isDirty } = form.formState;
  const { ConfirmLeaveDialog } = useUnsavedChangesPrompt(isDirty);

  const editMutation = useOpenFarmApiEndpointsFilamentsEditFilamentEndpoint({
    mutation: {
      onSuccess: (response) => {
        const saved = response.data as FilamentResponse;
        form.reset({
          variants: saved.variants ?? [],
          profileAssignments: saved.profileAssignments ?? [],
        });
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
        toast.success('Saved.');
      },
      onError: async (error: unknown) => {
        toast.error(
          await extractErrorDetail(error, 'Could not save filament.'),
        );
      },
    },
  });

  const variants = form.watch('variants');

  function onSubmit(values: EditDraft) {
    editMutation.mutate({
      id: filamentId,
      data: {
        newVariants: values.variants
          .filter((variant) => !variant.id)
          .map((variant) => ({
            colorName: variant.colorName,
            colorHex: variant.colorHex,
          })),
        // Full replace: always send the whole current list, or the server wipes assignments.
        profileAssignments: values.profileAssignments,
      },
    });
  }

  return (
    <div className="flex flex-1 flex-col gap-6">
      <PageHeader
        above={backLink}
        title="Edit filament"
        description={`${filament.vendorName} · ${filament.material} · ${filament.materialType}`}
      />

      <form
        onSubmit={form.handleSubmit(onSubmit)}
        className="flex flex-1 flex-col gap-6"
      >
        <FieldGroup className="flex-1 gap-6">
          <Card>
            <CardHeader>
              <CardTitle>Details</CardTitle>
            </CardHeader>
            <CardContent>
              <dl className="grid grid-cols-1 gap-4 text-sm sm:grid-cols-3">
                {[
                  ['Vendor', filament.vendorName],
                  ['Material name', filament.material],
                  ['Material type', filament.materialType],
                ].map(([label, value]) => (
                  <div key={label} className="flex flex-col gap-1">
                    <dt className="text-muted-foreground">{label}</dt>
                    <dd className="font-medium">{value}</dd>
                  </div>
                ))}
              </dl>
            </CardContent>
          </Card>

          <VariantsCard
            variants={variants}
            onAdd={(variant) =>
              form.setValue('variants', [...variants, variant], {
                shouldDirty: true,
              })
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
                { shouldDirty: true },
              );
            }}
          />

          <ProfileAssignmentsCard
            materialType={filament.materialType ?? ''}
            variants={variants}
            assignments={form.watch('profileAssignments')}
            issues={issues}
            onChange={(value) =>
              form.setValue('profileAssignments', value, { shouldDirty: true })
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
                <Button
                  type="submit"
                  disabled={!isDirty || editMutation.isPending}
                >
                  {editMutation.isPending && (
                    <Spinner data-icon="inline-start" />
                  )}
                  {editMutation.isPending ? 'Saving…' : 'Save'}
                </Button>
              </>
            }
          />
        </FieldGroup>
      </form>

      <ConfirmLeaveDialog />
    </div>
  );
}
