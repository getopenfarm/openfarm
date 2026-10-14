import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
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
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { PartResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPartsGetPartEndpointQueryKey,
  getOpenFarmApiEndpointsPartsListPartsEndpointQueryKey,
  useOpenFarmApiEndpointsPartsEditPartEndpoint,
  useOpenFarmApiEndpointsPartsGetPartEndpoint,
} from '@/lib/api/generated/parts/parts';
import { AddOrReplacePartDialog } from './components/AddOrReplacePartDialog';
import { PartEstimate } from './components/PartEstimate';
import { PartEstimationSource } from './components/PartEstimationSource';
import {
  type DisplayFilamentRequirement,
  PartFilamentRequirementsList,
} from './components/PartFilamentRequirementsList';
import {
  type PartRemovalNoticeData,
  PartRemovalToast,
} from './components/PartRemovalToast';
import { PartThumbnailGallery } from './components/PartThumbnailGallery';
import { RemovePartButton } from './components/RemovePartButton';
import { usePartsHub } from './hooks/usePartsHub';

type Narrowing = { variantId: string | null; profileId: string | null };

const schema = z.object({
  name: z.string().trim().min(1, 'Name is required'),
  tags: z.string(),
  unitsPerPrint: z
    .number({ error: 'Enter a whole number of at least 1.' })
    .int('Enter a whole number of at least 1.')
    .min(1, 'Enter a whole number of at least 1.'),
  narrowing: z.record(
    z.string(),
    z.object({
      variantId: z.string().nullable(),
      profileId: z.string().nullable(),
    }),
  ),
});

type FormValues = z.infer<typeof schema>;

function toFormValues(part: PartResponse | undefined): FormValues {
  return {
    name: part?.name ?? '',
    tags: (part?.tags ?? []).join(', '),
    unitsPerPrint: part?.unitsPerPrint ?? 1,
    narrowing: Object.fromEntries(
      (part?.filamentRequirements ?? []).map((requirement) => [
        requirement.id ?? '',
        {
          variantId: requirement.narrowFilamentVariantId ?? null,
          profileId: requirement.narrowFilamentProfileId ?? null,
        },
      ]),
    ),
  };
}

export function PartDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  usePartsHub();

  const [removalNotice, setRemovalNotice] = useState<PartRemovalNoticeData>();
  useEffect(() => {
    void id;
    setRemovalNotice(undefined);
  }, [id]);
  const [replaceOpen, setReplaceOpen] = useState(false);
  const [reprocessOpen, setReprocessOpen] = useState(false);

  const partQuery = useOpenFarmApiEndpointsPartsGetPartEndpoint(id ?? '');
  const part = partQuery.data?.data as PartResponse | undefined;
  const queryError: unknown = partQuery.error;

  // `values` re-syncs the draft when the server copy changes (hub refetch); keepDirtyValues
  // keeps whatever the operator is mid-way through editing.
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    values: toFormValues(part),
    resetOptions: { keepDirtyValues: true },
  });
  const { isDirty } = form.formState;
  const { ConfirmLeaveDialog } = useUnsavedChangesPrompt(isDirty);
  const narrowing = form.watch('narrowing');

  const editMutation = useOpenFarmApiEndpointsPartsEditPartEndpoint({
    mutation: {
      onSuccess: (response) => {
        const saved = response.data as PartResponse;
        form.reset(toFormValues(saved));
        queryClient.setQueryData(
          getOpenFarmApiEndpointsPartsGetPartEndpointQueryKey(id ?? ''),
          response,
        );
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsPartsListPartsEndpointQueryKey(),
        });
        queryClient.invalidateQueries({ queryKey: ['/api/v1/tags'] });
        toast.success('Saved.');
      },
      onError: async (error) =>
        toast.error(await extractErrorDetail(error, 'Could not save part.')),
    },
  });

  function setNarrowing(requirementId: string, next: Narrowing) {
    form.setValue(
      'narrowing',
      { ...form.getValues('narrowing'), [requirementId]: next },
      { shouldDirty: true },
    );
  }

  function onSubmit(values: FormValues) {
    if (!part?.id) return;
    const tags = values.tags
      .split(',')
      .map((tag) => tag.trim())
      .filter(Boolean);
    const filamentRequirements = (part.filamentRequirements ?? [])
      .filter((requirement) => {
        const draft = values.narrowing[requirement.id ?? ''];
        return (
          draft &&
          (draft.variantId !== (requirement.narrowFilamentVariantId ?? null) ||
            draft.profileId !== (requirement.narrowFilamentProfileId ?? null))
        );
      })
      .map((requirement) => {
        const draft = values.narrowing[requirement.id ?? ''];
        return {
          requirementId: requirement.id,
          narrowFilamentVariantId: draft.variantId,
          narrowFilamentProfileId: draft.profileId,
        };
      });
    editMutation.mutate({
      id: part.id,
      data: {
        name: values.name,
        tags,
        unitsPerPrint: values.unitsPerPrint,
        filamentRequirements,
      },
    });
  }

  const requirements: DisplayFilamentRequirement[] =
    part?.filamentRequirements?.map((requirement) => {
      const serverVariant = requirement.narrowFilamentVariantId ?? null;
      const serverProfile = requirement.narrowFilamentProfileId ?? null;
      const draft = narrowing?.[requirement.id ?? ''] ?? {
        variantId: serverVariant,
        profileId: serverProfile,
      };
      return {
        key: requirement.id ?? '',
        materialType: requirement.materialType,
        detectedColorHex: requirement.detectedColorHex,
        detectedSubBrand: requirement.detectedSubBrand,
        narrowFilamentVariantId: draft.variantId,
        narrowFilamentVariantUnresolved:
          draft.variantId === serverVariant &&
          requirement.narrowFilamentVariantUnresolved,
        narrowFilamentProfileId: draft.profileId,
        narrowFilamentProfileUnresolved:
          draft.profileId === serverProfile &&
          requirement.narrowFilamentProfileUnresolved,
      };
    }) ?? [];

  const backLink = (
    <BackLink to="/parts" className="mb-1">
      Back to parts
    </BackLink>
  );
  const notFound = (
    <EmptyState
      title="Part not found"
      description="It may have been removed, or the link is out of date."
      action={
        <Button variant="outline" onClick={() => navigate('/parts')}>
          Back to parts
        </Button>
      }
    />
  );

  return (
    <div className="flex flex-1 flex-col gap-6">
      {partQuery.isLoading ? (
        <>
          <PageHeader above={backLink} title="Part" />
          <Skeleton className="h-64 w-full" />
        </>
      ) : partQuery.isError ? (
        <>
          <PageHeader above={backLink} title="Part" />
          {queryError instanceof Response && queryError.status === 404 ? (
            notFound
          ) : (
            <ErrorState
              title="Could not load this part"
              onRetry={() => partQuery.refetch()}
            />
          )}
        </>
      ) : !part ? (
        <>
          <PageHeader above={backLink} title="Part" />
          {notFound}
        </>
      ) : (
        <>
          <PageHeader
            above={backLink}
            title={part.name}
            description={
              <span className="font-mono">
                {part.fileFormat} ·{' '}
                {part.nozzleDiameterMm != null
                  ? `${part.nozzleDiameterMm} mm nozzle`
                  : 'Unknown nozzle'}
                {part.requiresSlicing ? ' · requires slicing' : ' · pre-sliced'}
              </span>
            }
            action={
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  variant="outline"
                  onClick={() => setReprocessOpen(true)}
                >
                  Reprocess
                </Button>
                <Button variant="outline" onClick={() => setReplaceOpen(true)}>
                  Replace file
                </Button>
                <RemovePartButton
                  key={part.id}
                  onNotice={setRemovalNotice}
                  partId={part.id}
                  partName={part.name}
                  onRemoved={() => navigate('/parts')}
                />
              </div>
            }
          />

          <PartRemovalToast notice={removalNotice} />
          {part.printRequirementsStatus === 'Incomplete' && (
            <p className="text-sm text-muted-foreground">
              Print requirements incomplete — reprocess after printer setup, or
              replace the file with complete metadata before production.
            </p>
          )}
          {(part.processingWarnings ?? []).length > 0 && (
            <div
              role="status"
              className="rounded-lg border p-3 text-sm text-muted-foreground"
            >
              {part.processingWarnings?.map((warning) => (
                <p key={warning}>{warning}</p>
              ))}
            </div>
          )}
          <Form {...form}>
            <form
              onSubmit={form.handleSubmit(onSubmit)}
              className="flex flex-1 flex-col gap-6"
            >
              <div className="flex flex-1 flex-col gap-6">
                <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1.2fr_1fr]">
                  <div className="flex flex-col gap-4">
                    <Card>
                      <CardHeader>
                        <CardTitle>Details</CardTitle>
                      </CardHeader>
                      <CardContent className="flex flex-col gap-4">
                        <FormField
                          control={form.control}
                          name="name"
                          render={({ field }) => (
                            <FormItem>
                              <FormLabel>Name</FormLabel>
                              <FormControl>
                                <Input {...field} />
                              </FormControl>
                              <FormMessage />
                            </FormItem>
                          )}
                        />
                        <FormField
                          control={form.control}
                          name="tags"
                          render={({ field }) => (
                            <FormItem>
                              <FormLabel>Tags</FormLabel>
                              <FormControl>
                                <Input
                                  placeholder="Brackets, Enclosure (comma separated)"
                                  {...field}
                                />
                              </FormControl>
                              <FormMessage />
                            </FormItem>
                          )}
                        />
                      </CardContent>
                    </Card>
                    <Card>
                      <CardHeader>
                        <CardTitle>Part preview</CardTitle>
                      </CardHeader>
                      <CardContent>
                        <PartThumbnailGallery
                          thumbnails={part.thumbnails}
                          plateCount={part.plateCount}
                          tileSize="8rem"
                        />
                      </CardContent>
                    </Card>
                    <Card>
                      <CardHeader>
                        <CardTitle>Estimate</CardTitle>
                      </CardHeader>
                      <CardContent>
                        <PartEstimationSource estimation={part.estimation} />
                        <PartEstimate
                          status={part.estimateStatus}
                          minutes={part.estimatedPrintTimeMinutes}
                          grams={part.estimatedFilamentWeightGrams}
                        />
                      </CardContent>
                    </Card>
                    <Card>
                      <CardHeader>
                        <CardTitle>Production</CardTitle>
                      </CardHeader>
                      <CardContent>
                        <FormField
                          control={form.control}
                          name="unitsPerPrint"
                          render={({ field }) => (
                            <FormItem>
                              <FormLabel>Units per print</FormLabel>
                              <FormControl>
                                <Input
                                  type="number"
                                  {...field}
                                  value={
                                    Number.isNaN(field.value) ? '' : field.value
                                  }
                                  onChange={(event) =>
                                    field.onChange(event.target.valueAsNumber)
                                  }
                                />
                              </FormControl>
                              <FormDescription>
                                Used to calculate whole plates for future jobs.
                              </FormDescription>
                              <FormMessage />
                            </FormItem>
                          )}
                        />
                      </CardContent>
                    </Card>
                  </div>
                  <Card className="self-start">
                    <CardHeader>
                      <CardTitle>Production filament</CardTitle>
                    </CardHeader>
                    <CardContent>
                      <PartFilamentRequirementsList
                        requirements={requirements}
                        onNarrow={(key, variantId) =>
                          setNarrowing(String(key), {
                            variantId,
                            profileId: null,
                          })
                        }
                        onNarrowToProfile={(requirementId, profileId) =>
                          setNarrowing(requirementId, {
                            variantId: null,
                            profileId,
                          })
                        }
                      />
                    </CardContent>
                  </Card>
                </div>

                <FormFooter
                  end={
                    <>
                      <Button
                        type="button"
                        variant="outline"
                        onClick={() => navigate('/parts')}
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
              </div>
            </form>
          </Form>
          <ConfirmLeaveDialog />

          <AddOrReplacePartDialog
            partId={part.id}
            partName={part.name}
            open={replaceOpen}
            onOpenChange={setReplaceOpen}
          />

          <AddOrReplacePartDialog
            partId={part.id}
            partName={part.name}
            mode="reprocess"
            open={reprocessOpen}
            onOpenChange={setReprocessOpen}
          />
        </>
      )}
    </div>
  );
}
