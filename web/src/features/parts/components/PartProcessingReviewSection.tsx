import { AlertTriangleIcon, LoaderIcon } from 'lucide-react';
import type { PartProcessingJobPreviewResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  type DisplayFilamentRequirement,
  PartFilamentRequirementsList,
} from './PartFilamentRequirementsList';
import { PartThumbnailGallery } from './PartThumbnailGallery';

interface PartProcessingReviewSectionProps {
  status: string | undefined;
  preview: PartProcessingJobPreviewResponse | null | undefined;
  errorMessage?: string | null;
  overrides: Record<number, string | null>;
  onNarrow: (
    requirementIndex: number,
    filamentVariantId: string | null,
  ) => void;
}

/**
 * The in-dialog preview panel (FR-009a). Nozzle diameter/material appear as soon as `status`
 * reaches "ReadyForReview" — the estimate/thumbnails may still be resolving at that point for a
 * requires-slicing file (research.md §3b), shown here as a "still resolving" indicator rather
 * than blocking the rest of the panel from rendering.
 */
export function PartProcessingReviewSection({
  status,
  preview,
  errorMessage,
  overrides,
  onNarrow,
}: PartProcessingReviewSectionProps) {
  if (status === 'Failed') {
    return (
      <div className="flex items-start gap-2 rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">
        <AlertTriangleIcon className="mt-0.5 size-4 shrink-0" />
        <p>{errorMessage ?? 'Processing failed.'}</p>
      </div>
    );
  }

  if (status !== 'ReadyForReview' || !preview) {
    return (
      <div className="flex items-center gap-2 rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
        <LoaderIcon className="size-4 animate-spin" />
        Processing…
      </div>
    );
  }

  const requirements: DisplayFilamentRequirement[] = (
    preview.filamentRequirements ?? []
  ).map((requirement) => ({
    key: requirement.requirementIndex ?? 0,
    materialType: requirement.materialType,
    detectedColorHex: requirement.detectedColorHex,
    detectedSubBrand: requirement.detectedSubBrand,
    narrowFilamentVariantId:
      requirement.requirementIndex !== undefined
        ? (overrides[requirement.requirementIndex] ?? null)
        : null,
  }));

  const estimateStatus = preview.estimateStatus ?? 'Resolving';

  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-2 gap-3 text-sm">
        <div>
          <p className="text-muted-foreground">Nozzle diameter</p>
          <p className="font-medium">
            {preview.nozzleDiameterMm != null
              ? `${preview.nozzleDiameterMm} mm`
              : '—'}
          </p>
        </div>
        <div>
          <p className="text-muted-foreground">Estimated print</p>
          {estimateStatus === 'Available' ? (
            <p className="font-medium">
              {preview.estimatedPrintTimeMinutes} min ·{' '}
              {preview.estimatedFilamentWeightGrams} g
            </p>
          ) : (
            <p className="flex items-center gap-1.5 font-medium text-muted-foreground">
              <LoaderIcon className="size-3.5 animate-spin" />
              Estimating…
            </p>
          )}
        </div>
      </div>

      <div>
        <p className="mb-1.5 text-sm text-muted-foreground">Thumbnails</p>
        <PartThumbnailGallery
          thumbnails={preview.thumbnails}
          plateCount={preview.plateCount ?? undefined}
        />
      </div>

      <div>
        <p className="mb-1.5 text-sm text-muted-foreground">
          Filament requirements
        </p>
        <PartFilamentRequirementsList
          requirements={requirements}
          onNarrow={(key, filamentVariantId) =>
            onNarrow(Number(key), filamentVariantId)
          }
        />
      </div>
    </div>
  );
}
