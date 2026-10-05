import { Upload } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogDescription,
  DialogScrollableContent,
  DialogTitle,
} from '@/components/ui/dialog';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
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
}: AddOrReplacePartDialogProps) {
  const {
    file,
    jobId,
    reprocess,
    isReprocessPending,
    status,
    job,
    overrides,
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
    onConfirmed: () => onOpenChange(false),
  });

  function handleCancel() {
    cancel();
    onOpenChange(false);
  }

  const canConfirm = status === 'ReadyForReview';

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
              <PartProcessingReviewSection
                status={status}
                preview={job?.preview}
                errorMessage={job?.errorMessage}
                overrides={overrides}
                onNarrow={handleNarrow}
              />
            ) : (
              <div className="flex flex-col gap-3">
                <p className="text-sm text-muted-foreground">
                  Reprocess the saved file using current printer and profile
                  configuration. Review the results before saving.
                </p>
                <Button onClick={reprocess} disabled={isReprocessPending}>
                  {isReprocessPending ? 'Starting…' : 'Start reprocessing'}
                </Button>
              </div>
            )
          ) : !file ? (
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
