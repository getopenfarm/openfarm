import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { Upload } from 'lucide-react';
import type { ChangeEvent, DragEvent } from 'react';
import { useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogDescription,
  DialogScrollableContent,
  DialogTitle,
} from '@/components/ui/dialog';
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
import type {
  PartProcessingJobResponse,
  PartProcessingJobSubmittedResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPartProcessingJobsGetPartProcessingJobEndpointQueryKey,
  useOpenFarmApiEndpointsPartProcessingJobsConfirmPartProcessingJobEndpoint,
  useOpenFarmApiEndpointsPartProcessingJobsDiscardPartProcessingJobEndpoint,
  useOpenFarmApiEndpointsPartProcessingJobsGetPartProcessingJobEndpoint,
} from '@/lib/api/generated/part-processing-jobs/part-processing-jobs';
import {
  getOpenFarmApiEndpointsPartsGetPartEndpointQueryKey,
  getOpenFarmApiEndpointsPartsListPartsEndpointQueryKey,
  useOpenFarmApiEndpointsPartsCreatePartEndpoint,
  useOpenFarmApiEndpointsPartsReplacePartFileEndpoint,
} from '@/lib/api/generated/parts/parts';
import { useOpenFarmApiEndpointsSettingsGetSlicingDefaultsEndpoint } from '@/lib/api/generated/settings/settings';
import { cn } from '@/lib/utils';
import { PartProcessingReviewSection } from './PartProcessingReviewSection';

const ACCEPTED_EXTENSIONS = '.stl,.3mf,.gcode,.gco,.g';

// Whether a file requires slicing is only known server-side, after classification (an STL always
// does, a 3MF only if it's not already sliced, a G-code export never does) — so the picker below
// is shown before a file is even chosen (the dialog fires Create/Replace immediately on selection,
// research.md §11) rather than gated on file type. The backend ignores both fields for a file that
// turns out not to require slicing either way (contracts/slicer-management-api.md), so offering the
// choice a moment early is harmless.
const USE_DEFAULT_SLICER = '__use-organization-default__';

function slicerSelectionKey(slicer: { engine?: string; version?: string }) {
  return `${slicer.engine}::${slicer.version}`;
}

/** Backend errors surface as RFC 7807 ProblemDetails on the raw fetch Response httpClient
 * throws (CLAUDE.md: "Errors surface uniformly as RFC 7807 ProblemDetails") — extracts the
 * operator-facing `detail` (e.g. "No registered printer has a linked Machine Profile with a
 * known nozzle diameter.") instead of a generic message, matching ProfilesPage's convention. */
async function extractErrorDetail(
  error: unknown,
  fallback: string,
): Promise<string> {
  if (error instanceof Response) {
    try {
      const problem = await error.json();
      if (typeof problem?.detail === 'string' && problem.detail.length > 0) {
        return problem.detail;
      }
    } catch {
      // Response body wasn't JSON — fall through to the generic fallback.
    }
  }
  return fallback;
}

const addSchema = z.object({
  name: z.string().min(1, 'Name is required'),
  tags: z.string(),
});

type AddFormValues = z.infer<typeof addSchema>;

interface AddOrReplacePartDialogProps {
  mode: 'add' | 'replace';
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Required for mode="replace" — the existing part's file/derived data are replaced against
   * this id; its name/tags are never touched (FR-004). */
  partId?: string;
  partName?: string;
}

/**
 * The single continuous Add/Replace Part dialog (research.md §11): attaching a file fires
 * Create/Replace immediately — no filamentId field exists anywhere (research.md §13), material
 * always resolves from the file itself. For Add mode, name/tags stay editable in this same
 * dialog while the job processes in the background. Save enables as soon as the job reaches
 * ReadyForReview (nozzle diameter + material resolved) — it does not wait for the estimate or
 * thumbnails to finish (FR-009c). Cancel discards whatever job is in flight.
 */
export function AddOrReplacePartDialog({
  mode,
  open,
  onOpenChange,
  partId,
  partName,
}: AddOrReplacePartDialogProps) {
  const [file, setFile] = useState<File | null>(null);
  const [jobId, setJobId] = useState<string | null>(null);
  const [overrides, setOverrides] = useState<Record<number, string | null>>({});
  const [isDragging, setIsDragging] = useState(false);
  const [slicerSelection, setSlicerSelection] = useState(USE_DEFAULT_SLICER);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const queryClient = useQueryClient();

  const slicingDefaultsQuery =
    useOpenFarmApiEndpointsSettingsGetSlicingDefaultsEndpoint();
  const availableSlicers = slicingDefaultsQuery.data?.data?.available ?? [];
  const effectiveDefault = slicingDefaultsQuery.data?.data?.default;

  const form = useForm<AddFormValues>({
    resolver: zodResolver(addSchema),
    defaultValues: { name: '', tags: '' },
  });

  const jobQuery =
    useOpenFarmApiEndpointsPartProcessingJobsGetPartProcessingJobEndpoint(
      jobId ?? '',
      { query: { enabled: jobId !== null, refetchInterval: false } },
    );
  const job = jobQuery.data?.data as PartProcessingJobResponse | undefined;
  const status = job?.status;

  const createMutation = useOpenFarmApiEndpointsPartsCreatePartEndpoint({
    mutation: {
      onSuccess: (response) => {
        const data = response.data as PartProcessingJobSubmittedResponse;
        setJobId(data.jobId ?? null);
      },
      onError: async (error) => {
        toast.error(
          await extractErrorDetail(error, 'Could not process this file.'),
        );
        setFile(null);
      },
    },
  });

  const replaceMutation = useOpenFarmApiEndpointsPartsReplacePartFileEndpoint({
    mutation: {
      onSuccess: (response) => {
        const data = response.data as PartProcessingJobSubmittedResponse;
        setJobId(data.jobId ?? null);
      },
      onError: async (error) => {
        toast.error(
          await extractErrorDetail(error, 'Could not process this file.'),
        );
        setFile(null);
      },
    },
  });

  const confirmMutation =
    useOpenFarmApiEndpointsPartProcessingJobsConfirmPartProcessingJobEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success(mode === 'add' ? 'Part added.' : 'File replaced.');
          queryClient.invalidateQueries({
            queryKey: getOpenFarmApiEndpointsPartsListPartsEndpointQueryKey(),
          });
          if (partId) {
            queryClient.invalidateQueries({
              queryKey:
                getOpenFarmApiEndpointsPartsGetPartEndpointQueryKey(partId),
            });
          }
          resetAndClose();
        },
        onError: async (error) => {
          toast.error(
            await extractErrorDetail(error, 'Could not save the part.'),
          );
        },
      },
    });

  const discardMutation =
    useOpenFarmApiEndpointsPartProcessingJobsDiscardPartProcessingJobEndpoint();

  function resetAndClose() {
    onOpenChange(false);
    setFile(null);
    setJobId(null);
    setOverrides({});
    setSlicerSelection(USE_DEFAULT_SLICER);
    form.reset({ name: '', tags: '' });
  }

  function processFile(selectedFile: File) {
    setFile(selectedFile);
    setJobId(null);
    setOverrides({});

    const [slicerEngine, slicerVersion] =
      slicerSelection === USE_DEFAULT_SLICER
        ? [undefined, undefined]
        : slicerSelection.split('::');

    if (mode === 'add') {
      createMutation.mutate({
        data: { file: selectedFile, slicerEngine, slicerVersion },
      });
    } else if (partId) {
      replaceMutation.mutate({
        id: partId,
        data: { file: selectedFile, slicerEngine, slicerVersion },
      });
    }
  }

  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    const selectedFile = event.target.files?.[0];
    event.target.value = '';
    if (selectedFile) {
      processFile(selectedFile);
    }
  }

  function openFileDialog() {
    fileInputRef.current?.click();
  }

  function handleDragOver(event: DragEvent<HTMLButtonElement>) {
    event.preventDefault();
    setIsDragging(true);
  }

  function handleDragLeave(event: DragEvent<HTMLButtonElement>) {
    event.preventDefault();
    setIsDragging(false);
  }

  function handleDrop(event: DragEvent<HTMLButtonElement>) {
    event.preventDefault();
    setIsDragging(false);
    const droppedFile = event.dataTransfer.files?.[0];
    if (droppedFile) {
      processFile(droppedFile);
    }
  }

  function handleNarrow(
    requirementIndex: number,
    filamentVariantId: string | null,
  ) {
    setOverrides((prev) => ({
      ...prev,
      [requirementIndex]: filamentVariantId,
    }));
  }

  function buildOverridesPayload() {
    const entries = Object.entries(overrides).filter(
      ([, value]) => value !== null,
    );
    return entries.length > 0
      ? entries.map(([requirementIndex, narrowFilamentVariantId]) => ({
          requirementIndex: Number(requirementIndex),
          narrowFilamentVariantId,
        }))
      : undefined;
  }

  function handleConfirm(values: AddFormValues) {
    if (!jobId) {
      return;
    }
    const filamentRequirementOverrides = buildOverridesPayload();
    if (mode === 'add') {
      const tags = values.tags
        .split(',')
        .map((tag) => tag.trim())
        .filter(Boolean);
      confirmMutation.mutate({
        id: jobId,
        data: {
          name: values.name,
          tags: tags.length > 0 ? tags : undefined,
          filamentRequirementOverrides,
        },
      });
    } else {
      confirmMutation.mutate({
        id: jobId,
        data: { filamentRequirementOverrides },
      });
    }
  }

  function handleCancel() {
    if (jobId && status !== 'Confirmed' && status !== 'Discarded') {
      discardMutation.mutate(
        { id: jobId },
        {
          onSettled: () => {
            queryClient.removeQueries({
              queryKey:
                getOpenFarmApiEndpointsPartProcessingJobsGetPartProcessingJobEndpointQueryKey(
                  jobId,
                ),
            });
          },
        },
      );
    }
    resetAndClose();
  }

  const canConfirm =
    status === 'ReadyForReview' &&
    (mode === 'replace' || form.watch('name').trim().length > 0);

  const title =
    mode === 'add' ? 'Add part' : `Replace file for ${partName ?? 'part'}`;

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => (next ? onOpenChange(true) : handleCancel())}
    >
      <DialogScrollableContent
        className="max-w-2xl"
        header={
          <>
            <DialogTitle>{title}</DialogTitle>
            <DialogDescription>
              Attach an STL, 3MF, or G-code file — material and nozzle diameter
              resolve automatically from the file and your fleet, no selection
              needed. Save enables as soon as those are ready; the
              print-time/weight estimate may still be resolving in the
              background.
            </DialogDescription>
          </>
        }
        footer={
          <>
            <Button variant="outline" onClick={handleCancel}>
              Cancel
            </Button>
            <Button
              onClick={form.handleSubmit(handleConfirm)}
              disabled={!canConfirm || confirmMutation.isPending}
            >
              {confirmMutation.isPending ? 'Saving…' : 'Save'}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          {!file ? (
            <div className="flex flex-col gap-4">
              {availableSlicers.length > 0 && (
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor="part-slicer-override">Slicer</Label>
                  <Select
                    value={slicerSelection}
                    onValueChange={setSlicerSelection}
                  >
                    <SelectTrigger id="part-slicer-override" className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={USE_DEFAULT_SLICER}>
                        Organization default
                        {effectiveDefault
                          ? ` (${effectiveDefault.engine} ${effectiveDefault.version})`
                          : ''}
                      </SelectItem>
                      {availableSlicers.map((slicer) => (
                        <SelectItem
                          key={slicerSelectionKey(slicer)}
                          value={slicerSelectionKey(slicer)}
                        >
                          {slicer.engine} {slicer.version}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <p className="text-muted-foreground text-xs">
                    Only used for files that need slicing (STL or an unsliced
                    3MF) — ignored otherwise.
                  </p>
                </div>
              )}
              <div className="flex flex-col gap-2">
                <button
                  type="button"
                  aria-label="Choose a part file, or drag and drop it here"
                  onClick={openFileDialog}
                  onDragOver={handleDragOver}
                  onDragEnter={handleDragOver}
                  onDragLeave={handleDragLeave}
                  onDrop={handleDrop}
                  className={cn(
                    'flex cursor-pointer flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed p-8 text-center transition-colors',
                    isDragging
                      ? 'border-primary bg-primary/5'
                      : 'border-input hover:border-primary/50',
                  )}
                >
                  <Upload className="size-6 text-muted-foreground" />
                  <p className="text-sm">
                    <span className="font-medium text-primary">
                      Click to browse
                    </span>{' '}
                    or drag and drop a file
                  </p>
                  <p className="text-xs text-muted-foreground">
                    STL, 3MF, or G-code
                  </p>
                </button>
                <input
                  ref={fileInputRef}
                  type="file"
                  accept={ACCEPTED_EXTENSIONS}
                  onChange={handleFileChange}
                  className="hidden"
                />
              </div>
            </div>
          ) : (
            <>
              <p className="text-sm text-muted-foreground">
                <span className="font-medium text-foreground">{file.name}</span>{' '}
                — {(file.size / 1024).toFixed(0)} KB
              </p>

              {mode === 'add' && (
                <Form {...form}>
                  <div className="flex flex-col gap-4">
                    <FormField
                      control={form.control}
                      name="name"
                      render={({ field }) => (
                        <FormItem>
                          <FormLabel>Name</FormLabel>
                          <FormControl>
                            <Input
                              autoFocus
                              placeholder="Widget Bracket"
                              {...field}
                            />
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
                </Form>
              )}

              <PartProcessingReviewSection
                status={status}
                preview={job?.preview}
                errorMessage={job?.errorMessage}
                overrides={overrides}
                onNarrow={handleNarrow}
              />
            </>
          )}
        </div>
      </DialogScrollableContent>
    </Dialog>
  );
}
