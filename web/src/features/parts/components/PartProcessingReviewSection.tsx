import { AlertTriangleIcon, ImageOffIcon, LoaderIcon } from 'lucide-react';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import type {
  PartProcessingJobPreviewResponse,
  PlateResponse,
  ThumbnailResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { formatMm } from '@/lib/formatUnits';
import { filamentGrams, PartEstimate } from './PartEstimate';
import {
  type DisplayFilamentRequirement,
  PartFilamentRequirementsList,
} from './PartFilamentRequirementsList';
import { PartThumbnailGallery, plateLabel } from './PartThumbnailGallery';

interface PartProcessingReviewSectionProps {
  status: string | undefined;
  preview: PartProcessingJobPreviewResponse | null | undefined;
  errorMessage?: string | null;
  overrides: Record<number, string | null>;
  onNarrow: (
    requirementIndex: number,
    filamentVariantId: string | null,
  ) => void;
  /** The plate the part prints; asked only when the file offers several. */
  plateNumbers: number[];
  onPlateNumbersChange: (plateNumbers: number[]) => void;
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
  plateNumbers,
  onPlateNumbersChange,
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

  // A part prints one plate: the review shows that plate. A job from before plates were recorded has none and shows the file.
  const plates = preview.plates ?? [];
  const plate =
    plates.length === 1
      ? plates[0]
      : plates.find(
          (p) => plateNumbers.length === 1 && p.number === plateNumbers[0],
        );
  const shown = plate ?? (plates.length === 0 ? preview : undefined);
  const requirements: DisplayFilamentRequirement[] = (
    shown?.filamentRequirements ?? []
  ).map((requirement) => ({
    key: requirement.requirementIndex ?? 0,
    estimatedGrams: requirement.estimatedGrams,
    materialType: requirement.materialType,
    detectedColorHex: requirement.detectedColorHex,
    detectedSubBrand: requirement.detectedSubBrand,
    narrowFilamentVariantId:
      requirement.requirementIndex !== undefined
        ? Object.hasOwn(overrides, requirement.requirementIndex)
          ? overrides[requirement.requirementIndex]
          : requirement.narrowFilamentVariantId
        : requirement.narrowFilamentVariantId,
    narrowFilamentProfileId:
      requirement.requirementIndex !== undefined &&
      Object.hasOwn(overrides, requirement.requirementIndex)
        ? null
        : requirement.narrowFilamentProfileId,
  }));

  const estimateStatus = preview.estimateStatus;

  return (
    <div className="flex flex-col gap-4">
      <div className="grid grid-cols-2 gap-3 text-sm">
        {preview.estimation?.machineModel && (
          <div>
            <p className="text-muted-foreground">Printer model</p>
            <p className="font-medium">{preview.estimation.machineModel}</p>
          </div>
        )}
        <div>
          <p className="text-muted-foreground">Nozzle diameter</p>
          <p className="font-medium">
            {preview.nozzleDiameterMm != null
              ? formatMm(preview.nozzleDiameterMm)
              : 'Unknown'}
          </p>
        </div>
        <div>
          <p className="text-muted-foreground">Estimated print</p>
          {shown ? (
            <PartEstimate
              status={estimateStatus}
              minutes={shown.estimatedPrintTimeMinutes}
              grams={filamentGrams(
                requirements,
                shown.estimatedFilamentWeightGrams,
              )}
            />
          ) : (
            <p className="text-sm text-muted-foreground">Choose a plate.</p>
          )}
        </div>
      </div>

      {(preview.processingWarnings ?? []).length > 0 && (
        <div
          role="status"
          className="rounded-lg border p-3 text-sm text-muted-foreground"
        >
          {preview.processingWarnings?.map((warning) => (
            <p key={warning}>{warning}</p>
          ))}
        </div>
      )}
      {preview.printRequirementsStatus === 'Incomplete' && (
        <p className="text-sm text-muted-foreground">
          You can save this part and use it in assemblies. Print requirements
          must be completed before production.
        </p>
      )}
      {plates.length > 1 ? (
        <div>
          <p className="mb-1.5 text-sm text-muted-foreground">Plate</p>
          <PlatePicker
            plates={plates}
            thumbnails={preview.thumbnails}
            plateNumbers={plateNumbers}
            onChange={onPlateNumbersChange}
          />
        </div>
      ) : (
        <div>
          <p className="mb-1.5 text-sm text-muted-foreground">
            {plate ? plateLabel(plate) : 'Thumbnails'}
          </p>
          <PartThumbnailGallery
            thumbnails={preview.thumbnails?.filter(
              (thumbnail) =>
                !plate || thumbnail.plateIndex === (plate.number ?? 1) - 1,
            )}
          />
        </div>
      )}

      {shown && (
        <div>
          <p className="mb-1.5 text-sm text-muted-foreground">
            Filament requirements
          </p>
          {requirements.some(
            (requirement) => requirement.narrowFilamentProfileId,
          ) && (
            <p className="mb-2 text-xs text-muted-foreground">
              Existing filament profile selections will be preserved unless you
              change or clear a selection.
            </p>
          )}
          <PartFilamentRequirementsList
            requirements={requirements}
            onNarrow={(key, filamentVariantId) =>
              onNarrow(Number(key), filamentVariantId)
            }
          />
        </div>
      )}
    </div>
  );
}

function PlatePicker({
  plates,
  thumbnails,
  plateNumbers,
  onChange,
}: {
  plates: PlateResponse[];
  thumbnails: ThumbnailResponse[] | undefined;
  plateNumbers: number[];
  onChange: (plateNumbers: number[]) => void;
}) {
  const byIndex = new Map(
    (thumbnails ?? []).map((thumbnail) => [thumbnail.plateIndex, thumbnail]),
  );
  return (
    <ToggleGroup
      type="single"
      variant="outline"
      value={plateNumbers.length === 1 ? String(plateNumbers[0]) : ''}
      onValueChange={(value) => onChange(value ? [Number(value)] : [])}
      className="flex-wrap"
    >
      {plates.map((plate) => {
        const thumbnail = byIndex.get((plate.number ?? 1) - 1);
        return (
          <ToggleGroupItem
            key={plate.number}
            value={String(plate.number)}
            className="h-auto flex-col items-start gap-1 p-2"
          >
            <span className="flex size-16 items-center justify-center overflow-hidden rounded-md bg-muted/40">
              {thumbnail?.thumbnailBase64 ? (
                <img
                  src={`data:image/png;base64,${thumbnail.thumbnailBase64}`}
                  alt=""
                  className="size-full object-contain"
                />
              ) : (
                <ImageOffIcon className="size-4 text-muted-foreground" />
              )}
            </span>
            <span className="text-xs font-medium">{plateLabel(plate)}</span>
            <PartEstimate
              minutes={plate.estimatedPrintTimeMinutes}
              grams={plate.estimatedFilamentWeightGrams}
            />
          </ToggleGroupItem>
        );
      })}
    </ToggleGroup>
  );
}
