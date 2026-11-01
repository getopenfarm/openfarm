import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { TrashIcon } from 'lucide-react';
import { useRef } from 'react';
import {
  type Control,
  useFieldArray,
  useForm,
  useWatch,
} from 'react-hook-form';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import { BackLink } from '@/components/back-link/BackLink';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { EditorSectionHeader } from '@/components/editor-section/EditorSectionHeader';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Alert, AlertDescription } from '@/components/ui/alert';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import {
  DropdownMenuGroup,
  DropdownMenuItem,
} from '@/components/ui/dropdown-menu';
import { FieldGroup } from '@/components/ui/field';
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
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { usePrintedTargetCatalog } from '@/features/assemblies/components/useBomCatalog';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { useOpenFarmApiEndpointsProductionRunsCreateProductionRunEndpoint } from '@/lib/api/generated/production-runs/production-runs';
import { formatEstimateMetric } from '@/lib/printEstimates';
import {
  RunDetailsFields,
  runDetailsRequest,
  runDetailsSchema,
} from './components/RunDetailsFields';
import { RunPlanPreview } from './components/RunPlanPreview';
import { TargetPicker, TargetThumbnail } from './components/TargetPicker';
import {
  type EstimateLine,
  type RunEstimate,
  type RunLineEstimate,
  useRunEstimate,
} from './components/useRunEstimate';
import { invalidateProductionQueries } from './hooks/useProductionHub';

const AUTOMATIC = 'automatic';
const isWhole = (v: string, min: number) =>
  Number.isInteger(Number(v)) && Number(v) >= min;

const lineSchema = z
  .object({
    targetKind: z.enum(['Part', 'Assembly']),
    targetId: z.string(),
    targetName: z.string(),
    quantity: z.string(),
    quantityBasis: z.enum(['Plates', 'Units', 'Sets']),
    printerId: z.string(),
  })
  .superRefine((line, ctx) => {
    if (!isWhole(line.quantity, 1))
      ctx.addIssue({
        code: 'custom',
        path: ['quantity'],
        message: '1 or more.',
      });
  });

const schema = runDetailsSchema.extend({
  lines: z.array(lineSchema).min(1, 'Add at least one part or assembly.'),
});

type FormValues = z.infer<typeof schema>;
type LineValues = FormValues['lines'][number];

const toEstimateLine = (line: LineValues, lineId: string): EstimateLine => ({
  lineId,
  targetKind: line.targetKind,
  targetId: line.targetId,
  quantity: Number(line.quantity) || 0,
  quantityBasis: line.quantityBasis,
  assignmentMode: line.printerId === AUTOMATIC ? 'Unassigned' : 'Pinned',
  pinnedPrinterId: line.printerId === AUTOMATIC ? null : line.printerId,
});

function LineRow({
  control,
  index,
  line,
  printers,
  thumbnail,
  perPlate,
  onRemove,
  estimate,
  disabled,
}: {
  control: Control<FormValues>;
  index: number;
  line: LineValues;
  printers: { id?: string; name?: string }[];
  thumbnail?: string | null;
  perPlate?: number;
  onRemove: () => void;
  estimate?: RunLineEstimate;
  disabled: boolean;
}) {
  return (
    <li
      aria-label={line.targetName}
      className="grid min-w-0 grid-cols-[2.5rem_minmax(0,1fr)_auto] gap-x-3 gap-y-4 py-5 first:pt-0 last:pb-0"
    >
      <TargetThumbnail kind={line.targetKind} thumbnailBase64={thumbnail} />
      <div className="min-w-0 pt-0.5">
        <span className="block truncate font-medium">{line.targetName}</span>
        <span className="text-xs text-muted-foreground">
          {line.targetKind === 'Assembly'
            ? 'Assembly'
            : `${perPlate ?? 1} per plate`}
        </span>
      </div>
      <DataTableRowActions label={line.targetName} disabled={disabled}>
        <DropdownMenuGroup>
          <DropdownMenuItem onSelect={onRemove}>
            <TrashIcon />
            Remove {line.targetName}
          </DropdownMenuItem>
        </DropdownMenuGroup>
      </DataTableRowActions>

      <FieldGroup className="col-span-3 flex flex-row flex-wrap items-start gap-4">
        <div className="flex flex-col gap-1.5">
          <div className="flex flex-wrap items-start gap-3">
            <FormField
              control={control}
              name={`lines.${index}.quantity`}
              render={({ field }) => (
                <FormItem
                  className="w-28"
                  data-disabled={disabled || undefined}
                >
                  <FormLabel className="text-xs">
                    {line.targetKind === 'Assembly'
                      ? 'Sets'
                      : line.quantityBasis === 'Plates'
                        ? 'Plates'
                        : 'Unit target'}
                  </FormLabel>
                  <FormControl>
                    <Input
                      type="number"
                      min={1}
                      step={1}
                      disabled={disabled}
                      {...field}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            {line.targetKind === 'Part' && (
              <FormField
                control={control}
                name={`lines.${index}.quantityBasis`}
                render={({ field }) => (
                  <FormItem data-disabled={disabled || undefined}>
                    <FormLabel className="text-xs">Quantity basis</FormLabel>
                    <FormControl>
                      <ToggleGroup
                        type="single"
                        disabled={disabled}
                        variant="outline"
                        value={field.value}
                        onValueChange={(value) => {
                          if (value) field.onChange(value);
                        }}
                      >
                        <ToggleGroupItem value="Plates">Plates</ToggleGroupItem>
                        <ToggleGroupItem value="Units">
                          Unit target
                        </ToggleGroupItem>
                      </ToggleGroup>
                    </FormControl>
                  </FormItem>
                )}
              />
            )}
          </div>
        </div>

        <FormField
          control={control}
          name={`lines.${index}.printerId`}
          render={({ field }) => (
            <FormItem
              className="w-48 max-w-full"
              data-disabled={disabled || undefined}
            >
              <FormLabel className="text-xs">Printer</FormLabel>
              <Select
                disabled={disabled}
                value={field.value}
                onValueChange={field.onChange}
              >
                <FormControl>
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                </FormControl>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value={AUTOMATIC}>Automatic</SelectItem>
                    {printers.map((printer) => (
                      <SelectItem key={printer.id} value={printer.id ?? ''}>
                        {printer.name}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
            </FormItem>
          )}
        />
      </FieldGroup>
      <div className="col-span-3 min-w-0">
        <RunPlanPreview
          name={line.targetName}
          estimate={estimate}
          sets={
            line.targetKind === 'Assembly' ? Number(line.quantity) : undefined
          }
        />
      </div>
    </li>
  );
}

function Summary({
  lines,
  ids,
  estimate,
}: {
  lines: LineValues[];
  ids: string[];
  estimate: RunEstimate;
}) {
  return (
    <Card className="min-w-0 xl:sticky xl:top-4">
      <CardHeader>
        <CardTitle>Summary</CardTitle>
        <CardDescription>
          What this run will make and send to the queue.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4 text-sm">
        {lines.length === 0 ? (
          <EmptyState
            size="sm"
            bordered={false}
            title="Nothing planned yet"
            description="Add a part or assembly to see production totals."
          />
        ) : (
          <>
            <ul className="flex flex-col gap-2" aria-label="Requested targets">
              {lines.map((line, index) => (
                <li key={ids[index]} className="flex justify-between gap-4">
                  <span className="min-w-0 break-words">{line.targetName}</span>
                  <span className="shrink-0 tabular-nums">
                    {line.quantity || '—'}{' '}
                    {line.quantityBasis === 'Sets'
                      ? 'sets'
                      : line.quantityBasis === 'Plates'
                        ? 'plates'
                        : 'units'}
                  </span>
                </li>
              ))}
            </ul>
            <div aria-live="polite" aria-busy={estimate.isPending}>
              {estimate.isError ? (
                <Alert variant="destructive">
                  <AlertDescription>
                    Production totals are unavailable. Retry the item previews
                    to continue.
                  </AlertDescription>
                </Alert>
              ) : estimate.isInvalid ? (
                <Alert>
                  <AlertDescription>
                    Enter a valid quantity for every item to see production
                    totals.
                  </AlertDescription>
                </Alert>
              ) : !estimate.isReady ? (
                <div role="status">
                  <span className="sr-only">Updating production totals…</span>
                  <Skeleton className="h-28 w-full" />
                </div>
              ) : (
                <dl className="grid grid-cols-[minmax(0,1fr)_auto] gap-x-4 gap-y-3 tabular-nums">
                  <dt className="text-muted-foreground">Plates</dt>
                  <dd className="text-right font-medium">{estimate.plates}</dd>
                  <dt className="text-muted-foreground">
                    Printed component units
                  </dt>
                  <dd className="text-right font-medium">{estimate.units}</dd>
                  <dt className="text-muted-foreground">Machine time</dt>
                  <dd className="text-right font-medium">
                    {formatEstimateMetric(estimate.time, 'time')}
                  </dd>
                  <dt className="text-muted-foreground">Filament</dt>
                  <dd className="text-right font-medium">
                    {formatEstimateMetric(estimate.filament, 'filament')}
                  </dd>
                </dl>
              )}
            </div>
            {estimate.isReady &&
              ((estimate.time?.missing ?? 0) > 0 ||
                (estimate.filament?.missing ?? 0) > 0) && (
                <p className="text-muted-foreground">
                  Estimates exclude components with missing data. Machine time
                  is the combined printing time, not a completion date.
                </p>
              )}
            {estimate.isReady && estimate.approximate && (
              <p className="text-muted-foreground">
                Partial-plate estimates are proportional approximations.
                Startup, purge, and travel may not scale linearly.
              </p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}

/** One draft: production targets first, optional details, and a shared live server plan. */
export function NewProductionRunPage() {
  const navigate = useNavigate();
  const submission = useRef({ key: '', id: '' });
  const submitting = useRef(false);
  const queryClient = useQueryClient();
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', dueDate: '', notes: '', lines: [] },
  });
  const { fields, append, remove } = useFieldArray({
    control: form.control,
    name: 'lines',
  });
  const lines = useWatch({ control: form.control, name: 'lines' }) ?? [];
  const preview = useRunEstimate(
    fields.map((field, index) =>
      toEstimateLine(lines[index] ?? field, field.id),
    ),
  );
  const pickerRef = useRef<HTMLButtonElement>(null);
  const printers =
    useOpenFarmApiEndpointsPrintersListFleetEndpoint().data?.data?.printers ??
    [];
  const catalog = usePrintedTargetCatalog();
  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );
  const create =
    useOpenFarmApiEndpointsProductionRunsCreateProductionRunEndpoint();

  async function onSubmit(values: FormValues) {
    if (!preview.isReady || submitting.current) return;
    submitting.current = true;
    try {
      const key = JSON.stringify(values);
      if (submission.current.key !== key)
        submission.current = { key, id: crypto.randomUUID() };
      const created = await create.mutateAsync({
        data: {
          ...runDetailsRequest(values),
          submissionId: submission.current.id,
          lines: values.lines.map((line, index) => ({
            targetKind: line.targetKind,
            targetId: line.targetId,
            quantity: Number(line.quantity),
            quantityBasis: line.quantityBasis,
            planVersion: preview.byLine.get(fields[index].id)?.plan?.version,
            assignmentMode:
              line.printerId === AUTOMATIC ? 'Unassigned' : 'Pinned',
            pinnedPrinterId:
              line.printerId === AUTOMATIC ? null : line.printerId,
          })),
        },
      });
      toast.success('Run added.');
      invalidateProductionQueries(queryClient);
      markClean();
      navigate(`/production-runs/${created.data?.id}`);
    } catch (error) {
      toast.error(await extractErrorDetail(error, 'Could not add run.'));
      await preview.refetch();
    } finally {
      submitting.current = false;
    }
  }

  const linesError =
    form.formState.errors.lines?.root?.message ??
    form.formState.errors.lines?.message;
  return (
    <Page className="flex-1">
      <PageHeader
        above={
          <BackLink to="/production-runs" className="mb-1">
            Back to runs
          </BackLink>
        }
        title="Add run"
      />

      <Form {...form}>
        <form
          onSubmit={form.handleSubmit(onSubmit)}
          className="flex flex-1 flex-col gap-6"
        >
          <div className="grid flex-1 items-start gap-6 xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
            <div className="flex min-w-0 flex-col gap-6">
              <Card>
                <EditorSectionHeader
                  title="Items to make"
                  count={fields.length}
                  description="Choose parts in units or plates, and assemblies in complete sets."
                  action={
                    <TargetPicker
                      shortcuts
                      triggerRef={pickerRef}
                      disabled={create.isPending}
                      onPick={(target) =>
                        append({
                          targetKind: target.kind,
                          targetId: target.id,
                          targetName: target.name,
                          quantity: '1',
                          quantityBasis:
                            target.kind === 'Assembly' ? 'Sets' : 'Units',
                          printerId: AUTOMATIC,
                        })
                      }
                    />
                  }
                />
                <CardContent className="flex min-w-0 flex-col gap-5">
                  {fields.length === 0 ? (
                    <EmptyState
                      size="sm"
                      bordered={false}
                      title="No items yet"
                      description="Add the parts and assemblies this run makes."
                    />
                  ) : (
                    <ul className="flex flex-col divide-y">
                      {fields.map((field, index) => {
                        const line = lines[index] ?? field;
                        const part = catalog.parts.find(
                          (p) => p.id === line.targetId,
                        );
                        return (
                          <LineRow
                            key={field.id}
                            control={form.control}
                            index={index}
                            line={line}
                            printers={printers}
                            thumbnail={part?.thumbnailBase64}
                            perPlate={part?.unitsPerPrint}
                            disabled={create.isPending}
                            estimate={preview.byLine.get(field.id)}
                            onRemove={() => {
                              remove(index);
                              requestAnimationFrame(() =>
                                pickerRef.current?.focus(),
                              );
                            }}
                          />
                        );
                      })}
                    </ul>
                  )}
                  {linesError && (
                    <p role="alert" className="mt-2 text-sm text-destructive">
                      {linesError}
                    </p>
                  )}
                </CardContent>
              </Card>
              <RunDetailsFields
                control={form.control}
                optional
                disabled={create.isPending}
              />
            </div>
            <Summary
              lines={lines}
              ids={fields.map((field) => field.id)}
              estimate={preview}
            />
          </div>

          <FormFooter
            end={
              <>
                <Button
                  type="button"
                  variant="outline"
                  disabled={create.isPending}
                  onClick={() => navigate('/production-runs')}
                >
                  Cancel
                </Button>
                <Button
                  permission="production-runs:create"
                  type="submit"
                  disabled={
                    create.isPending || (fields.length > 0 && !preview.isReady)
                  }
                >
                  {create.isPending && <Spinner data-icon="inline-start" />}
                  {create.isPending ? 'Adding…' : 'Add run'}
                </Button>
              </>
            }
          />
        </form>
      </Form>

      <ConfirmLeaveDialog />
    </Page>
  );
}
