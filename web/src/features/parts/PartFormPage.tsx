import { zodResolver } from '@hookform/resolvers/zod';
import { Upload } from 'lucide-react';
import { useEffect, useRef } from 'react';
import { useForm } from 'react-hook-form';
import { useNavigate } from 'react-router';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Spinner } from '@/components/ui/spinner';
import { useLibraryLocation } from '@/features/library/useLibraryLocation';
import { formatSlicer } from '@/features/slicing/formatSlicer';
import { SlicerReadinessPanel } from '@/features/slicing/SlicerReadinessPanel';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { formatFileSize } from '@/lib/formatUnits';
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
  const { folderId, location } = useLibraryLocation('parts');
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
    plateNumbers,
    setPlateNumbers,
    needsPlate,
  } = usePartFileProcessing({
    mode: 'add',
    folderId,
    onConfirmed: () => {
      markCleanRef.current();
      navigate(location);
    },
  });

  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    file !== null || form.formState.isDirty,
  );
  markCleanRef.current = markClean;

  // Name defaults to the file name; a name the user typed is kept.
  useEffect(() => {
    if (file && !form.getFieldState('name').isDirty) {
      form.setValue('name', file.name.replace(/(\.gcode)?\.[^.]+$/i, ''));
    }
  }, [file, form]);

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

  // Each part takes its plate's own estimate, so several wait for it.
  const split = plateNumbers.length > 1;
  const waitForEstimate = split && job?.preview?.estimateStatus === 'Resolving';
  const canConfirm =
    status === 'ReadyForReview' &&
    !needsPlate &&
    !waitForEstimate &&
    form.watch('name').trim().length > 0;

  return (
    <Page className="flex-1">
      <PageHeader
        above={
          <BackLink to={location} className="mb-1">
            Back to parts
          </BackLink>
        }
        title="Add part"
        description="Upload a file. Review metadata. Choose production filament."
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-1 flex-col gap-6"
        >
          <FieldGroup className="flex-1 gap-6">
            <Card>
              <CardHeader>
                <CardTitle>Part file and details</CardTitle>
              </CardHeader>
              <CardContent className="flex flex-col gap-4">
                {!file ? (
                  <div className="flex flex-col gap-4">
                    {availableSlicers.length > 0 && (
                      <Field className="flex flex-col gap-1.5">
                        <FieldLabel htmlFor="part-slicer-override">
                          Slicer
                        </FieldLabel>
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
                            <SelectGroup>
                              <SelectItem value={USE_DEFAULT_SLICER}>
                                Farm default
                                {effectiveDefault
                                  ? ` (${formatSlicer(effectiveDefault.engine, effectiveDefault.version)})`
                                  : ''}
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
                          Only used for files that need slicing (STL or an
                          unsliced 3MF) — ignored otherwise. The slicer uses
                          only its own profiles.
                        </p>
                        {(() => {
                          const [engine, version] =
                            slicerSelection === USE_DEFAULT_SLICER
                              ? [
                                  effectiveDefault?.engine,
                                  effectiveDefault?.version,
                                ]
                              : slicerSelection.split('::');
                          return engine && version ? (
                            <SlicerReadinessPanel
                              engine={engine}
                              version={version}
                            />
                          ) : null;
                        })()}
                      </Field>
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
                      — {formatFileSize(file.size)}
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
                              placeholder="e.g. brackets, enclosure"
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
                      plateNumbers={plateNumbers}
                      onPlateNumbersChange={setPlateNumbers}
                      multiple
                    />
                  </>
                )}
              </CardContent>
            </Card>

            <FormFooter
              end={
                <>
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => navigate(location)}
                  >
                    Cancel
                  </Button>
                  <Button
                    type="submit"
                    disabled={!canConfirm || isConfirmPending}
                  >
                    {isConfirmPending && <Spinner data-icon="inline-start" />}
                    {isConfirmPending
                      ? 'Adding…'
                      : split
                        ? `Add ${plateNumbers.length} parts`
                        : 'Add part'}
                  </Button>
                </>
              }
            />
          </FieldGroup>
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </Page>
  );
}
