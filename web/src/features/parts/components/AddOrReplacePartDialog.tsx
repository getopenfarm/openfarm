import { Upload } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogDescription,
  DialogScrollableContent,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { formatSlicer } from '@/features/slicing/formatSlicer';
import { SlicerReadinessPanel } from '@/features/slicing/SlicerReadinessPanel';
import { formatFileSize } from '@/lib/formatUnits';
import { cn } from '@/lib/utils';
import { PartProcessingReviewSection } from './PartProcessingReviewSection';
import { plateLabel } from './PartThumbnailGallery';
import {
  ACCEPTED_EXTENSIONS,
  slicerSelectionKey,
  USE_DEFAULT_SLICER,
  usePartFileProcessing,
} from './usePartFileProcessing';

interface AddOrReplacePartDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The existing part's file/derived data are replaced against this id; its name/tags are
   * never touched (FR-004). */
  partId?: string;
  partName?: string;
  mode?: 'replace' | 'reprocess';
  /** The slicer the part is pinned to: replacing or reprocessing keeps it unless another is chosen. */
  currentSlicer?: { engine?: string; version?: string } | null;
  /** The plate the part prints: kept when the file still has it. */
  currentPlateNumber?: number | null;
  currentPlateName?: string | null;
}

const SKIP = 'skip';

/**
 * Replace-file dialog (research.md §11, "reserved" modal per docs/UI_CONVENTIONS.md — a quick
 * action on an existing entity, not a page). Attaching a file fires Replace immediately; Save
 * enables as soon as the job reaches ReadyForReview (nozzle diameter + material resolved) — it
 * does not wait for the estimate or thumbnails to finish (FR-009c). Cancel discards whatever job
 * is in flight. The Add flow lives at `/parts/new` (`PartFormPage`), sharing this same
 * continuous background-processing behavior via `usePartFileProcessing`.
 */
export function AddOrReplacePartDialog({
  open,
  onOpenChange,
  partId,
  partName,
  mode = 'replace',
  currentSlicer,
  currentPlateNumber,
  currentPlateName,
}: AddOrReplacePartDialogProps) {
  const {
    file,
    jobId,
    reprocess,
    isReprocessPending,
    status,
    job,
    overrides,
    plateNumbers,
    setPlateNumbers,
    needsPlate,
    relatedParts,
    relatedPlates,
    setRelatedPlate,
    relatedReplacedCount,
    isDragging,
    slicerSelection,
    setSlicerSelection,
    availableSlicers,
    effectiveDefault,
    fileInputRef,
    handleFileChange,
    openFileDialog,
    handleDragOver,
    handleDragLeave,
    handleDrop,
    handleNarrow,
    confirm,
    cancel,
    isConfirmPending,
  } = usePartFileProcessing({
    mode,
    partId,
    currentPlateNumber,
    currentPlateName,
    onConfirmed: () => onOpenChange(false),
  });

  function handleCancel() {
    cancel();
    onOpenChange(false);
  }

  const plates = job?.preview?.plates ?? [];
  // Other parts sharing the file are replaced too once the estimate is in (each takes its own plate's).
  const waitForEstimate =
    relatedReplacedCount > 0 && job?.preview?.estimateStatus === 'Resolving';
  const canConfirm =
    status === 'ReadyForReview' && !needsPlate && !waitForEstimate;
  const chosen = plates.find((plate) => plate.number === plateNumbers[0]);
  const plateChanges =
    currentPlateNumber != null &&
    chosen != null &&
    (chosen.name && currentPlateName
      ? chosen.name !== currentPlateName
      : chosen.number !== currentPlateNumber);
  const unused = plates.filter(
    (plate) =>
      plate.number !== plateNumbers[0] &&
      !Object.values(relatedPlates).includes(plate.number ?? null),
  );
  const review = (
    <>
      <PartProcessingReviewSection
        status={status}
        preview={job?.preview}
        errorMessage={job?.errorMessage}
        overrides={overrides}
        onNarrow={handleNarrow}
        plateNumbers={plateNumbers}
        onPlateNumbersChange={setPlateNumbers}
      />
      {plateChanges && chosen && (
        <p role="status" className="text-sm text-kind-amber">
          Plate changes:{' '}
          {plateLabel({ number: currentPlateNumber, name: currentPlateName })} →{' '}
          {plateLabel(chosen)}
        </p>
      )}
      {status === 'ReadyForReview' && relatedParts.length > 0 && (
        <Field className="flex flex-col gap-1.5">
          <FieldLabel>Also replace</FieldLabel>
          <FieldDescription>
            {waitForEstimate
              ? 'Estimating… several parts need it.'
              : 'Parts sharing this file.'}
          </FieldDescription>
          {relatedParts.map((part) => {
            const partId = part.partId ?? '';
            const plateNumber = relatedPlates[partId];
            return (
              <div
                key={partId}
                className="grid grid-cols-[1fr_14rem] items-center gap-2 text-sm"
              >
                <span className="truncate" title={part.name}>
                  {part.name}
                </span>
                <Select
                  value={plateNumber != null ? String(plateNumber) : SKIP}
                  onValueChange={(value) =>
                    setRelatedPlate(
                      partId,
                      value === SKIP ? null : Number(value),
                    )
                  }
                >
                  <SelectTrigger
                    aria-label={`Plate for ${part.name}`}
                    className="w-full"
                  >
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectItem value={SKIP}>Skip · keep its file</SelectItem>
                      {plates.map((plate) => (
                        <SelectItem
                          key={plate.number}
                          value={String(plate.number)}
                        >
                          {plateLabel(plate)}
                        </SelectItem>
                      ))}
                    </SelectGroup>
                  </SelectContent>
                </Select>
              </div>
            );
          })}
          {unused.length > 0 && (
            <p className="text-xs text-muted-foreground">
              Not used: {unused.map(plateLabel).join(', ')}
            </p>
          )}
        </Field>
      )}
    </>
  );

  // Replacing or reprocessing keeps the part's slicer unless the operator picks another: say which one will be used.
  const [chosenEngine, chosenVersion] =
    slicerSelection === USE_DEFAULT_SLICER
      ? [
          (currentSlicer ?? effectiveDefault)?.engine,
          (currentSlicer ?? effectiveDefault)?.version,
        ]
      : slicerSelection.split('::');
  const slicerPicker = availableSlicers.length > 0 && (
    <Field className="flex flex-col gap-1.5">
      <FieldLabel htmlFor="part-slicer-override">Slicer</FieldLabel>
      <Select value={slicerSelection} onValueChange={setSlicerSelection}>
        <SelectTrigger id="part-slicer-override" className="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectItem value={USE_DEFAULT_SLICER}>
              {currentSlicer?.engine
                ? `Keep current slicer (${formatSlicer(currentSlicer.engine, currentSlicer.version)})`
                : `Farm default${
                    effectiveDefault
                      ? ` (${formatSlicer(effectiveDefault.engine, effectiveDefault.version)})`
                      : ''
                  }`}
            </SelectItem>
            {availableSlicers.map((slicer) => (
              <SelectItem
                key={slicerSelectionKey(slicer)}
                value={slicerSelectionKey(slicer)}
              >
                {formatSlicer(slicer.engine, slicer.version)}
              </SelectItem>
            ))}
          </SelectGroup>
        </SelectContent>
      </Select>
      <p className="text-muted-foreground text-xs">
        Only used for files that need slicing (STL or an unsliced 3MF) — ignored
        otherwise. The slicer uses only its own profiles.
      </p>
      {chosenEngine && chosenVersion && (
        <SlicerReadinessPanel engine={chosenEngine} version={chosenVersion} />
      )}
    </Field>
  );

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (next) {
          onOpenChange(true);
        } else {
          handleCancel();
        }
      }}
    >
      <DialogScrollableContent
        className="max-w-2xl"
        header={
          <>
            <DialogTitle>
              {mode === 'reprocess' ? 'Reprocess part' : 'Replace file'} for{' '}
              {partName ?? 'part'}
            </DialogTitle>
            <DialogDescription>
              Attach an STL, 3MF, or G-code file — material and nozzle diameter
              resolve automatically from the file and the fleet, no selection
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
              onClick={() => confirm()}
              disabled={!canConfirm || isConfirmPending}
            >
              {isConfirmPending
                ? 'Saving…'
                : relatedReplacedCount > 0
                  ? `Replace ${relatedReplacedCount + 1} parts`
                  : 'Save'}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          {mode === 'reprocess' ? (
            jobId ? (
              review
            ) : (
              <div className="flex flex-col gap-3">
                <p className="text-sm text-muted-foreground">
                  Reprocess the saved file using current printer and profile
                  configuration. Review the results before saving.
                </p>
                {slicerPicker}
                <Button onClick={reprocess} disabled={isReprocessPending}>
                  {isReprocessPending ? 'Starting…' : 'Start reprocessing'}
                </Button>
              </div>
            )
          ) : !file ? (
            <div className="flex flex-col gap-4">
              {slicerPicker}
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
                — {formatFileSize(file.size)}
              </p>

              {review}
            </>
          )}
        </div>
      </DialogScrollableContent>
    </Dialog>
  );
}
