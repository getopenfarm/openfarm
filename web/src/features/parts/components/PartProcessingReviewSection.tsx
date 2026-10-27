import { AlertTriangleIcon, ImageOffIcon, LoaderIcon } from 'lucide-react';
import { useId } from 'react';
import { Checkbox } from '@/components/ui/checkbox';
import { Field, FieldLabel } from '@/components/ui/field';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import type {
  PartProcessingJobPreviewResponse,
  PlateResponse,
  PreviewFilamentRequirementResponse,
  ThumbnailResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { formatMm } from '@/lib/formatUnits';
import { cn } from '@/lib/utils';
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
  /** Adding a file may take several plates: one part each. */
  multiple?: boolean;
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
  multiple = false,
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
  // Several plates narrow their filaments together: the API applies an override by the file's requirement index.
  const checked = plates.filter((p) => plateNumbers.includes(p.number ?? 1));
  const requirements: DisplayFilamentRequirement[] = (
    checked.length > 1
      ? combineRequirements(checked)
      : (shown?.filamentRequirements ?? [])
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
          {/* Several plates wait for the estimate too: each part takes its own plate's. */}
          {shown || estimateStatus === 'Resolving' ? (
            <PartEstimate
              status={estimateStatus}
              minutes={shown?.estimatedPrintTimeMinutes}
              grams={filamentGrams(
                requirements,
                shown?.estimatedFilamentWeightGrams,
              )}
            />
          ) : (
            <p className="text-sm text-muted-foreground">
              {plateNumbers.length > 1
                ? `${plateNumbers.length} parts, one per plate`
                : 'Choose a plate.'}
            </p>
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
          {!multiple && (
            <p className="mb-1.5 text-sm text-muted-foreground">Plate</p>
          )}
          <PlatePicker
            plates={plates}
            thumbnails={preview.thumbnails}
            plateNumbers={plateNumbers}
            onChange={onPlateNumbersChange}
            multiple={multiple}
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

      {(shown || checked.length > 1) && (
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

/** Each of the plates' filaments once, by the file's requirement index; grams summed over the plates. */
function combineRequirements(plates: PlateResponse[]) {
  const byIndex = new Map<number, PreviewFilamentRequirementResponse>();
  for (const requirement of plates.flatMap(
    (plate) => plate.filamentRequirements ?? [],
  )) {
    const index = requirement.requirementIndex ?? 0;
    const seen = byIndex.get(index);
    byIndex.set(
      index,
      seen
        ? {
            ...seen,
            estimatedGrams:
              seen.estimatedGrams != null && requirement.estimatedGrams != null
                ? seen.estimatedGrams + requirement.estimatedGrams
                : null,
          }
        : requirement,
    );
  }
  return [...byIndex.values()].sort(
    (a, b) => (a.requirementIndex ?? 0) - (b.requirementIndex ?? 0),
  );
}

function PlateName({ plate }: { plate: PlateResponse }) {
  return (
    <span className="truncate text-xs font-medium" title={plateLabel(plate)}>
      {plateLabel(plate)}
    </span>
  );
}

function PlateThumb({
  thumbnail,
  className,
}: {
  thumbnail: ThumbnailResponse | undefined;
  className?: string;
}) {
  return (
    <span
      className={cn(
        'flex aspect-square items-center justify-center overflow-hidden rounded-md bg-muted/40',
        className,
      )}
    >
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
  );
}

function PlatePicker({
  plates,
  thumbnails,
  plateNumbers,
  onChange,
  multiple,
}: {
  plates: PlateResponse[];
  thumbnails: ThumbnailResponse[] | undefined;
  plateNumbers: number[];
  onChange: (plateNumbers: number[]) => void;
  multiple: boolean;
}) {
  const id = useId();
  const byIndex = new Map(
    (thumbnails ?? []).map((thumbnail) => [thumbnail.plateIndex, thumbnail]),
  );
  const thumbnailOf = (plate: PlateResponse) =>
    byIndex.get((plate.number ?? 1) - 1);

  if (!multiple) {
    return (
      <ToggleGroup
        type="single"
        variant="outline"
        value={plateNumbers.length === 1 ? String(plateNumbers[0]) : ''}
        onValueChange={(value) => onChange(value ? [Number(value)] : [])}
        className="flex-wrap"
      >
        {plates.map((plate) => (
          <ToggleGroupItem
            key={plate.number}
            value={String(plate.number)}
            className="h-auto w-36 flex-col items-stretch gap-1.5 p-2 text-left font-normal"
          >
            <PlateThumb thumbnail={thumbnailOf(plate)} />
            <PlateName plate={plate} />
          </ToggleGroupItem>
        ))}
      </ToggleGroup>
    );
  }

  // Adding takes every plate by default: one part each. Unchecked plates fade.
  const all = plates.map((plate) => plate.number ?? 1);
  return (
    <div className="flex flex-col gap-2">
      <Field orientation="horizontal">
        <Checkbox
          id={`${id}-all`}
          checked={plateNumbers.length === all.length}
          onCheckedChange={(checked) => onChange(checked === true ? all : [])}
        />
        <FieldLabel htmlFor={`${id}-all`} className="font-normal">
          All plates
        </FieldLabel>
      </Field>
      <div className="grid grid-cols-[repeat(auto-fill,9rem)] gap-2">
        {plates.map((plate) => {
          const number = plate.number ?? 1;
          return (
            <FieldLabel key={number} htmlFor={`${id}-${number}`}>
              <Field className="gap-1.5">
                <PlateThumb
                  thumbnail={thumbnailOf(plate)}
                  className="opacity-40 transition-opacity group-has-data-checked/field-label:opacity-100 motion-reduce:transition-none"
                />
                <span className="flex min-w-0 items-center gap-2">
                  <Checkbox
                    id={`${id}-${number}`}
                    checked={plateNumbers.includes(number)}
                    onCheckedChange={(checked) =>
                      onChange(
                        checked === true
                          ? [...plateNumbers, number].sort((a, b) => a - b)
                          : plateNumbers.filter((n) => n !== number),
                      )
                    }
                  />
                  <PlateName plate={plate} />
                </span>
              </Field>
            </FieldLabel>
          );
        })}
      </div>
    </div>
  );
}
