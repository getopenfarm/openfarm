import { Upload } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogDescription,
  DialogScrollableContent,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldLabel } from '@/components/ui/field';
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
}

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
    onConfirmed: () => onOpenChange(false),
  });

  function handleCancel() {
    cancel();
    onOpenChange(false);
  }

  const canConfirm = status === 'ReadyForReview' && !needsPlate;
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
      {currentPlateNumber != null &&
        plateNumbers.length === 1 &&
        plateNumbers[0] !== currentPlateNumber && (
          <p role="status" className="text-sm text-kind-amber">
            Plate changes: {currentPlateNumber} → {plateNumbers[0]}
          </p>
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
              {isConfirmPending ? 'Saving…' : 'Save'}
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
