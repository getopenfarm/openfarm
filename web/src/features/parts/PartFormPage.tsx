import { zodResolver } from '@hookform/resolvers/zod';
import { Upload } from 'lucide-react';
import { useEffect, useRef } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate } from 'react-router';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
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
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { cn } from '@/lib/utils';
import { PartProcessingReviewSection } from './components/PartProcessingReviewSection';
import {
  ACCEPTED_EXTENSIONS,
  slicerSelectionKey,
  USE_DEFAULT_SLICER,
  usePartFileProcessing,
} from './components/usePartFileProcessing';
import { usePartsHub } from './hooks/usePartsHub';

const schema = z.object({
  name: z.string().min(1, 'Name is required'),
  tags: z.string(),
});

type FormValues = z.infer<typeof schema>;

export function PartFormPage() {
  const navigate = useNavigate();
  usePartsHub();

  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', tags: '' },
  });

  // onConfirmed fires from inside usePartFileProcessing's own onSuccess, which runs before
  // useUnsavedChangesPrompt (declared below) exists for this render — a ref bridges the two so
  // the navigate() below is exempted from the still-stale-at-that-instant isDirty guard, matching
  // the pattern every other migrated form uses for its post-save navigate call.
  const markCleanRef = useRef<() => void>(() => {});

  const {
    file,
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
    jobId,
    isConfirmPending,
  } = usePartFileProcessing({
    mode: 'add',
    onConfirmed: () => {
      markCleanRef.current();
      navigate('/parts');
    },
  });

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    file !== null || form.formState.isDirty,
  );
  markCleanRef.current = markClean;

  // Per spec Edge Cases: confirming navigate-away while a file is still processing cancels the
  // pending add, matching today's modal-close behavior. A ref keeps this effect's cleanup
  // reading fresh values without re-registering (and re-firing) on every render.
  const latestRef = useRef({ jobId, status, cancel });
  latestRef.current = { jobId, status, cancel };
  useEffect(() => {
    return () => {
      const { jobId, status, cancel } = latestRef.current;
      if (jobId && status !== 'Confirmed' && status !== 'Discarded') {
        cancel();
      }
    };
  }, []);

  function onSubmit(values: FormValues) {
    confirm(values);
  }

  const canConfirm =
    status === 'ReadyForReview' && form.watch('name').trim().length > 0;

  return (
    <div className="flex flex-col gap-6">
      <BackLink to="/parts">Back to parts</BackLink>

      <PageHeader
        title="Add part"
        description="Attach an STL, 3MF, or G-code file — material and nozzle diameter resolve automatically from the file and your fleet, no selection needed. Save enables as soon as those are ready; the print-time/weight estimate may still be resolving in the background."
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-col gap-6"
        >
          <Card>
            <CardContent className="flex flex-col gap-4">
              {!file ? (
                <div className="flex flex-col gap-4">
                  {availableSlicers.length > 0 && (
                    <div className="flex flex-col gap-1.5">
                      <Label htmlFor="part-slicer-override">Slicer</Label>
                      <Select
                        value={slicerSelection}
                        onValueChange={setSlicerSelection}
                      >
                        <SelectTrigger
                          id="part-slicer-override"
                          className="w-full"
                        >
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
                        Only used for files that need slicing (STL or an
                        unsliced 3MF) — ignored otherwise.
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
                    <span className="font-medium text-foreground">
                      {file.name}
                    </span>{' '}
                    — {(file.size / 1024).toFixed(0)} KB
                  </p>

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

                  <PartProcessingReviewSection
                    status={status}
                    preview={job?.preview}
                    errorMessage={job?.errorMessage}
                    overrides={overrides}
                    onNarrow={handleNarrow}
                  />
                </>
              )}
            </CardContent>
          </Card>

          <FormFooter
            start={
              <Button
                type="button"
                variant="ghost"
                onClick={() => navigate('/parts')}
              >
                Cancel
              </Button>
            }
            end={
              <Button type="submit" disabled={!canConfirm || isConfirmPending}>
                {isConfirmPending ? 'Saving…' : 'Add part'}
              </Button>
            }
          />
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </div>
  );
}
