import { useQueryClient } from '@tanstack/react-query';
import type { ChangeEvent, DragEvent } from 'react';
import { useRef, useState } from 'react';
import { toast } from 'sonner';
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
  useOpenFarmApiEndpointsPartsReprocessPartEndpoint,
} from '@/lib/api/generated/parts/parts';
import { useOpenFarmApiEndpointsSettingsGetSlicingDefaultsEndpoint } from '@/lib/api/generated/settings/settings';

export const ACCEPTED_EXTENSIONS = '.stl,.3mf,.gcode,.gco,.g';

// Whether a file requires slicing is only known server-side, after classification (an STL always
// does, a 3MF only if it's not already sliced, a G-code export never does) — so the picker below
// is shown before a file is even chosen (Create/Replace fires immediately on selection,
// research.md §11) rather than gated on file type. The backend ignores both fields for a file that
// turns out not to require slicing either way (contracts/slicer-management-api.md), so offering the
// choice a moment early is harmless.
export const USE_DEFAULT_SLICER = '__use-organization-default__';

export function slicerSelectionKey(slicer: {
  engine?: string;
  version?: string;
}) {
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

export interface UsePartFileProcessingOptions {
  mode: 'add' | 'replace' | 'reprocess';
  /** Required for mode="replace" — the existing part's file/derived data are replaced against
   * this id; its name/tags are never touched (FR-004). */
  partId?: string;
  /** Called once a job is successfully confirmed, after this hook's own state/query
   * invalidation has already run — the caller decides what "done" means (close a dialog,
   * navigate away, etc.). */
  onConfirmed?: () => void;
}

export interface ConfirmValues {
  name: string;
  tags: string;
}

/**
 * Continuous background file-processing behavior (spec 007-part-library): attaching a file
 * fires Create/Replace immediately, and the job polls/resolves in the background while the
 * caller's own UI (name/tags fields for add, or nothing extra for replace) stays interactive.
 * Extracted so the add-mode page and the retained replace-mode dialog can't drift apart on this
 * specific, tricky async behavior (research.md "Split Part's add/replace modes").
 */
export function usePartFileProcessing({
  mode,
  partId,
  onConfirmed,
}: UsePartFileProcessingOptions) {
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

  const reprocessMutation = useOpenFarmApiEndpointsPartsReprocessPartEndpoint({
    mutation: {
      onSuccess: (response) =>
        setJobId(
          (response.data as PartProcessingJobSubmittedResponse).jobId ?? null,
        ),
      onError: async (error) =>
        toast.error(
          await extractErrorDetail(error, 'Could not reprocess this part.'),
        ),
    },
  });

  function reprocess() {
    if (partId && !jobId && !reprocessMutation.isPending)
      reprocessMutation.mutate({ id: partId });
  }

  const confirmMutation =
    useOpenFarmApiEndpointsPartProcessingJobsConfirmPartProcessingJobEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success(
            mode === 'add'
              ? 'Part added.'
              : mode === 'reprocess'
                ? 'Part reprocessed.'
                : 'File replaced.',
          );
          queryClient.invalidateQueries({
            queryKey: getOpenFarmApiEndpointsPartsListPartsEndpointQueryKey(),
          });
          if (partId) {
            queryClient.invalidateQueries({
              queryKey:
                getOpenFarmApiEndpointsPartsGetPartEndpointQueryKey(partId),
            });
          }
          reset();
          onConfirmed?.();
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

  function reset() {
    setFile(null);
    setJobId(null);
    setOverrides({});
    setSlicerSelection(USE_DEFAULT_SLICER);
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
    const entries = Object.entries(overrides);
    return entries.length > 0
      ? entries.map(([requirementIndex, narrowFilamentVariantId]) => ({
          requirementIndex: Number(requirementIndex),
          narrowFilamentVariantId: narrowFilamentVariantId ?? undefined,
        }))
      : undefined;
  }

  function confirm(values?: ConfirmValues) {
    if (!jobId) {
      return;
    }
    const filamentRequirementOverrides = buildOverridesPayload();
    if (mode === 'add' && values) {
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

  function cancel() {
    if (
      jobId &&
      status !== 'Confirmed' &&
      status !== 'Discarded' &&
      status !== 'Failed'
    ) {
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
    reset();
  }

  return {
    file,
    reprocess,
    isReprocessPending: reprocessMutation.isPending,
    jobId,
    job,
    status,
    overrides,
    isDragging,
    slicerSelection,
    setSlicerSelection,
    availableSlicers,
    effectiveDefault,
    fileInputRef,
    processFile,
    handleFileChange,
    openFileDialog,
    handleDragOver,
    handleDragLeave,
    handleDrop,
    handleNarrow,
    confirm,
    cancel,
    reset,
    isConfirmPending: confirmMutation.isPending,
  };
}
