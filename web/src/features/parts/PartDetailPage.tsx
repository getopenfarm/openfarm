import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { ListPlusIcon, TriangleAlertIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
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
import { Separator } from '@/components/ui/separator';
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
import { formatMinutes } from '@/lib/formatMinutes';
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
import { QueuePartDialog } from './components/QueuePartDialog';
import { RemovePartButton } from './components/RemovePartButton';
import { usePartsHub } from './hooks/usePartsHub';

const compactHeader = 'border-b px-[18px] py-3.5';
const compactTitle = 'text-sm font-semibold';

function formatBytes(bytes: number): string {
  const mb = bytes / 1_048_576;
  return mb >= 1
    ? `${mb.toFixed(1)} MB`
    : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

function PartFileFacts({ part }: { part: PartResponse }) {
  const facts: [string, string][] = [
    ['File', part.originalFileName ?? '—'],
    [
      'Size',
      part.fileSizeBytes != null ? formatBytes(part.fileSizeBytes) : '—',
    ],
    ['Format', part.fileFormat ?? '—'],
    ['Plates', String(part.plateCount ?? '—')],
    [
      'Nozzle',
      part.nozzleDiameterMm != null ? `${part.nozzleDiameterMm} mm` : 'Unknown',
    ],
  ];
  if (part.slicer?.engine) {
    facts.push([
      'Slicer',
      `${part.slicer.engine} ${part.slicer.version ?? ''}`,
    ]);
  }
  return (
    <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-sm md:grid-cols-[auto_1fr_auto_1fr]">
      {facts.map(([label, value]) => (
        <div key={label} className="contents">
          <dt className="text-muted-foreground">{label}</dt>
          <dd className="min-w-0 truncate" title={value}>
            {value}
          </dd>
        </div>
      ))}
    </dl>
  );
}

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
  const [queueOpen, setQueueOpen] = useState(false);

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
  const unitsPerPrint = form.watch('unitsPerPrint');

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

  const incomplete = part?.printRequirementsStatus !== 'Complete';
  const warnings = part?.processingWarnings ?? [];
  const minutes = part?.estimatedPrintTimeMinutes;
  const grams = part?.estimatedFilamentWeightGrams;
  const perUnit =
    unitsPerPrint > 1 && (minutes != null || grams != null)
      ? `About ${[
          minutes != null && formatMinutes(minutes / unitsPerPrint),
          grams != null && `${Math.round((grams / unitsPerPrint) * 10) / 10} g`,
        ]
          .filter(Boolean)
          .join(' and ')} per unit.`
      : undefined;

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
    <Page className="flex-1">
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
            title={
              <span className="flex flex-wrap items-center gap-3">
                {part.name}
                {incomplete ? (
                  <StatusBadge kind="amber" label="Incomplete" />
                ) : (
                  <StatusBadge kind="green" label="Ready to print" />
                )}
              </span>
            }
            description={
              <span className="font-mono">
                {part.fileFormat} ·{' '}
                {part.nozzleDiameterMm != null
                  ? `${part.nozzleDiameterMm} mm nozzle`
                  : 'Unknown nozzle'}
                {part.requiresSlicing ? ' · requires slicing' : ' · pre-sliced'}
                {(part.plateCount ?? 0) > 1
                  ? ` · ${part.plateCount} plates`
                  : ''}
              </span>
            }
            action={
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  disabled={incomplete}
                  title={
                    incomplete
                      ? 'Complete this part’s print requirements before queueing.'
                      : undefined
                  }
                  onClick={() => setQueueOpen(true)}
                >
                  <ListPlusIcon data-icon="inline-start" />
                  Send to queue
                </Button>
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
          {(incomplete || warnings.length > 0) && (
            <Alert role="status">
              <TriangleAlertIcon className="text-kind-amber" />
              <AlertTitle>
                {incomplete
                  ? 'Print requirements incomplete'
                  : 'Processing warnings'}
              </AlertTitle>
              <AlertDescription>
                {incomplete && (
                  <p>
                    Reprocess after printer setup, or replace the file with
                    complete metadata before production.
                  </p>
                )}
                {warnings.length > 0 && (
                  <ul className="list-disc pl-4">
                    {warnings.map((warning) => (
                      <li key={warning}>{warning}</li>
                    ))}
                  </ul>
                )}
              </AlertDescription>
            </Alert>
          )}
          <Form {...form}>
            <form
              onSubmit={form.handleSubmit(onSubmit)}
              className="flex flex-1 flex-col gap-4"
            >
              <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1.6fr_1fr]">
                <Card className="gap-0 py-0">
                  <CardHeader className={compactHeader}>
                    <CardTitle className={compactTitle}>Part</CardTitle>
                  </CardHeader>
                  <CardContent className="flex flex-col gap-4 px-[18px] py-4">
                    <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
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
                    </div>
                    <Separator />
                    <div className="grid grid-cols-1 items-start gap-4 sm:grid-cols-[1fr_auto]">
                      <PartEstimate
                        size="lg"
                        status={part.estimateStatus}
                        minutes={part.estimatedPrintTimeMinutes}
                        grams={part.estimatedFilamentWeightGrams}
                      />
                      <FormField
                        control={form.control}
                        name="unitsPerPrint"
                        render={({ field }) => (
                          <FormItem>
                            <FormLabel>Units per print</FormLabel>
                            <FormControl>
                              <Input
                                type="number"
                                className="w-28"
                                {...field}
                                value={
                                  Number.isNaN(field.value) ? '' : field.value
                                }
                                onChange={(event) =>
                                  field.onChange(event.target.valueAsNumber)
                                }
                              />
                            </FormControl>
                            <FormMessage />
                          </FormItem>
                        )}
                      />
                    </div>
                    <p className="-mt-2 text-sm text-muted-foreground">
                      {part.estimateUnavailableReason ??
                        perUnit ??
                        'Units per print sets how many whole plates future jobs need.'}
                    </p>
                    {part.estimation && (
                      <details className="text-sm">
                        <summary className="w-fit cursor-pointer text-muted-foreground select-none hover:text-foreground">
                          How this was estimated
                        </summary>
                        <div className="pt-2">
                          <PartEstimationSource estimation={part.estimation} />
                        </div>
                      </details>
                    )}
                    <Separator />
                    <PartFileFacts part={part} />
                  </CardContent>
                </Card>

                <Card className="gap-0 py-0 lg:self-start">
                  <CardHeader className={compactHeader}>
                    <CardTitle className={compactTitle}>
                      Preview
                      {(part.plateCount ?? 0) > 1 && (
                        <span className="ml-1.5 font-normal text-muted-foreground">
                          {part.plateCount} plates
                        </span>
                      )}
                    </CardTitle>
                  </CardHeader>
                  <CardContent className="px-[18px] py-4">
                    <PartThumbnailGallery
                      thumbnails={part.thumbnails}
                      plateCount={part.plateCount}
                      tileSize="10rem"
                    />
                  </CardContent>
                </Card>
              </div>

              <Card className="gap-0 py-0">
                <CardHeader className={compactHeader}>
                  <CardTitle className={compactTitle}>
                    Production filament
                  </CardTitle>
                </CardHeader>
                <CardContent className="px-[18px] py-4">
                  <PartFilamentRequirementsList
                    layout="grid"
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

          {queueOpen && (
            <QueuePartDialog part={part} onClose={() => setQueueOpen(false)} />
          )}
        </>
      )}
    </Page>
  );
}
