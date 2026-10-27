import { useQueryClient } from '@tanstack/react-query';
import type { ChangeEvent, DragEvent } from 'react';
import { useRef, useState } from 'react';
import { toast } from 'sonner';
import type {
  PartProcessingJobResponse,
  PartProcessingJobSubmittedResponse,
  PlateResponse,
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
export const USE_DEFAULT_SLICER = '__use-farm-default__';

/**
 * A part's plate in a (new) file: the plate with its name when no other plate has that name, else the plate with its
 * number unless that plate is named otherwise — plate numbers shift when plates are reordered or removed in the slicer,
 * names do not.
 */
export function matchPlate(
  plates: PlateResponse[],
  part: { plateNumber?: number | null; plateName?: string | null },
) {
  const named = plates.filter(
    (plate) => part.plateName && plate.name === part.plateName,
  );
  return named.length === 1
    ? named[0]
    : plates.find(
        (plate) =>
          plate.number === part.plateNumber &&
          (!plate.name || !part.plateName || plate.name === part.plateName),
      );
}

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
  /** The plate the part prints now: replacing or reprocessing keeps it when the file still has it. */
  currentPlateNumber?: number | null;
  currentPlateName?: string | null;
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
  currentPlateNumber,
  currentPlateName,
}: UsePartFileProcessingOptions) {
  const [file, setFile] = useState<File | null>(null);
  const [jobId, setJobId] = useState<string | null>(null);
  const [overrides, setOverrides] = useState<Record<number, string | null>>({});
  const [chosenPlates, setPlateNumbers] = useState<number[] | null>(null);
  const [chosenRelated, setChosenRelated] = useState<Record<
    string,
    number | null
  > | null>(null);
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

  // A part prints one plate: the ones chosen, else every plate when adding (one part each), else the file's only plate,
  // else the part's own plate when the file has it.
  const plates = job?.preview?.plates ?? [];
  const current = matchPlate(plates, {
    plateNumber: currentPlateNumber,
    plateName: currentPlateName,
  });
  const plateNumbers =
    chosenPlates ??
    (mode === 'add' || plates.length === 1
      ? plates.map((plate) => plate.number ?? 1)
      : current?.number != null
        ? [current.number]
        : []);
  const needsPlate = plates.length > 1 && plateNumbers.length === 0;

  // A replace can update the other parts sharing the file: each takes its own plate of the new one (null skips it).
  const relatedParts = job?.relatedParts ?? [];
  const relatedPlates =
    chosenRelated ??
    Object.fromEntries(
      relatedParts.map((part) => [
        part.partId ?? '',
        matchPlate(plates, part)?.number ?? null,
      ]),
    );
  function setRelatedPlate(partId: string, plateNumber: number | null) {
    setChosenRelated({ ...relatedPlates, [partId]: plateNumber });
  }
  const relatedReplaced = Object.entries(relatedPlates).filter(
    (entry): entry is [string, number] => entry[1] != null,
  );

  const createMutation = useOpenFarmApiEndpointsPartsCreatePartEndpoint({
    mutation: {
      onSuccess: (response) => {
        const data = response.data as PartProcessingJobSubmittedResponse;
        setJobId(data.jobId ?? null);
      },
      onError: async (error) => {
        toast.error(await extractErrorDetail(error, 'Could not process file.'));
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
        toast.error(await extractErrorDetail(error, 'Could not process file.'));
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
          await extractErrorDetail(error, 'Could not reprocess part.'),
        ),
    },
  });

  function reprocess() {
    if (partId && !jobId && !reprocessMutation.isPending) {
      // Nothing chosen keeps the part's own slicer; an explicit choice moves it.
      const [slicerEngine, slicerVersion] =
        slicerSelection === USE_DEFAULT_SLICER
          ? [undefined, undefined]
          : slicerSelection.split('::');
      reprocessMutation.mutate({
        id: partId,
        data: { slicerEngine, slicerVersion },
      });
    }
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
          toast.error(await extractErrorDetail(error, 'Could not save part.'));
        },
      },
    });

  const discardMutation =
    useOpenFarmApiEndpointsPartProcessingJobsDiscardPartProcessingJobEndpoint();

  function reset() {
    setFile(null);
    setJobId(null);
    setOverrides({});
    setPlateNumbers(null);
    setChosenRelated(null);
    setSlicerSelection(USE_DEFAULT_SLICER);
  }

  function processFile(selectedFile: File) {
    setFile(selectedFile);
    setJobId(null);
    setOverrides({});
    setPlateNumbers(null);
    setChosenRelated(null);

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
    const chosen =
      plates.length > 0 && plateNumbers.length > 0 ? plateNumbers : undefined;
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
          plateNumbers: chosen,
        },
      });
    } else {
      confirmMutation.mutate({
        id: jobId,
        data: {
          filamentRequirementOverrides,
          plateNumbers: chosen,
          relatedParts:
            relatedReplaced.length > 0
              ? relatedReplaced.map(([partId, plateNumber]) => ({
                  partId,
                  plateNumber,
                }))
              : undefined,
        },
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
    plateNumbers,
    setPlateNumbers,
    needsPlate,
    relatedParts,
    relatedPlates,
    setRelatedPlate,
    relatedReplacedCount: relatedReplaced.length,
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
