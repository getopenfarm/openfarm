import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient } from '@tanstack/react-query';
import { TrashIcon } from 'lucide-react';
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
import { EmptyState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardAction,
  CardContent,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
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
import { Switch } from '@/components/ui/switch';
import { useBomCatalog } from '@/features/assemblies/components/useBomCatalog';
import { useUnsavedChangesPrompt } from '@/hooks/useUnsavedChangesPrompt';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { useOpenFarmApiEndpointsProductionRunsCreateProductionRunEndpoint } from '@/lib/api/generated/production-runs/production-runs';
import { formatMinutes } from '@/lib/formatMinutes';
import { formatWeight } from '@/lib/formatUnits';
import { pluralize } from '@/lib/pluralize';
import {
  RunDetailsFields,
  runDetailsRequest,
  runDetailsSchema,
} from './components/RunDetailsFields';
import { RunSlicerReadiness } from './components/RunSlicerReadiness';
import { TargetPicker, TargetThumbnail } from './components/TargetPicker';
import { type EstimateLine, useRunEstimate } from './components/useRunEstimate';
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
    unlimited: z.boolean(),
    printerId: z.string(),
    cap: z.string(),
    ceiling: z.string(),
  })
  .superRefine((line, ctx) => {
    if (!line.unlimited && !isWhole(line.quantity, 1))
      ctx.addIssue({
        code: 'custom',
        path: ['quantity'],
        message: '1 or more.',
      });
    if (line.unlimited && !isWhole(line.cap, 1))
      ctx.addIssue({ code: 'custom', path: ['cap'], message: '1 or more.' });
    if (
      line.unlimited &&
      line.ceiling.trim() !== '' &&
      !isWhole(line.ceiling, 1)
    )
      ctx.addIssue({
        code: 'custom',
        path: ['ceiling'],
        message: '1 or more, or blank.',
      });
  });

const schema = runDetailsSchema.extend({
  lines: z.array(lineSchema).min(1, 'Add at least one part or assembly.'),
});

type FormValues = z.infer<typeof schema>;
type LineValues = FormValues['lines'][number];

const toEstimateLine = (line: LineValues): EstimateLine => ({
  targetKind: line.targetKind,
  targetId: line.targetId,
  quantity: line.unlimited ? null : Number(line.quantity) || 0,
});

/** Plates this line makes, as the server rounds them: whole plates per part. */
function LinePlates({ line }: { line: LineValues }) {
  const estimate = useRunEstimate([toEstimateLine(line)]);
  if (line.unlimited)
    return (
      <span className="text-xs text-muted-foreground">One plate at a time</span>
    );
  return (
    <span className="text-xs text-muted-foreground tabular-nums">
      {estimate.isPending ? '…' : `→ ${pluralize(estimate.plates, 'plate')}`}
    </span>
  );
}

function LineRow({
  control,
  index,
  line,
  printers,
  thumbnail,
  perPlate,
  onRemove,
}: {
  control: Control<FormValues>;
  index: number;
  line: LineValues;
  printers: { id?: string; name?: string }[];
  thumbnail?: string | null;
  perPlate?: number;
  onRemove: () => void;
}) {
  return (
    <li className="grid grid-cols-[2.5rem_minmax(0,1fr)_auto] gap-x-3 gap-y-3 py-3 first:pt-0 last:pb-0 xl:grid-cols-[2.5rem_minmax(0,1fr)_14rem_11rem_auto] xl:items-start">
      <TargetThumbnail kind={line.targetKind} thumbnailBase64={thumbnail} />
      <div className="min-w-0 pt-0.5">
        <span className="block truncate font-medium">{line.targetName}</span>
        <span className="text-xs text-muted-foreground">
          {line.targetKind === 'Assembly'
            ? 'Assembly'
            : `${perPlate ?? 1} per plate`}
        </span>
      </div>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        className="xl:order-last"
        aria-label={`Remove ${line.targetName}`}
        onClick={onRemove}
      >
        <TrashIcon />
      </Button>

      {/* One row of controls under the name; on wide screens its two cells join the line's own row. */}
      <div className="col-span-3 flex flex-wrap items-start gap-x-6 gap-y-3 pl-[3.25rem] xl:contents">
        <div className="flex flex-col gap-1.5">
          <div className="flex items-start gap-3">
            {line.unlimited ? (
              <div className="flex gap-2">
                <FormField
                  control={control}
                  name={`lines.${index}.cap`}
                  render={({ field }) => (
                    <FormItem className="w-24">
                      <FormLabel className="text-xs">
                        Printers at once
                      </FormLabel>
                      <FormControl>
                        <Input type="number" min={1} step={1} {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={control}
                  name={`lines.${index}.ceiling`}
                  render={({ field }) => (
                    <FormItem className="w-24">
                      <FormLabel className="text-xs">Stop at units</FormLabel>
                      <FormControl>
                        <Input
                          type="number"
                          min={1}
                          step={1}
                          placeholder="Never"
                          {...field}
                        />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              </div>
            ) : (
              <FormField
                control={control}
                name={`lines.${index}.quantity`}
                render={({ field }) => (
                  <FormItem className="w-28">
                    <FormLabel className="text-xs">
                      {line.targetKind === 'Assembly' ? 'Sets' : 'Units'}
                    </FormLabel>
                    <FormControl>
                      <Input type="number" min={1} step={1} {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            )}
            <FormField
              control={control}
              name={`lines.${index}.unlimited`}
              render={({ field }) => (
                <FormItem className="flex flex-col items-start gap-2">
                  <FormLabel className="text-xs">Unlimited</FormLabel>
                  <FormControl>
                    <Switch
                      checked={field.value}
                      onCheckedChange={field.onChange}
                    />
                  </FormControl>
                </FormItem>
              )}
            />
          </div>
          <LinePlates line={line} />
        </div>

        <FormField
          control={control}
          name={`lines.${index}.printerId`}
          render={({ field }) => (
            <FormItem className="w-48 xl:w-auto">
              <FormLabel className="text-xs">Printer</FormLabel>
              <Select value={field.value} onValueChange={field.onChange}>
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
      </div>
    </li>
  );
}

function Summary({ lines }: { lines: LineValues[] }) {
  const estimate = useRunEstimate(lines.map(toEstimateLine));
  const unlimited = lines.filter((l) => l.unlimited).length;
  const partIds = [
    ...new Set(
      lines.filter((l) => l.targetKind === 'Part').map((l) => l.targetId),
    ),
  ];
  return (
    <Card className="lg:sticky lg:top-4">
      <CardHeader>
        <CardTitle>Summary</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3 text-sm">
        {lines.length === 0 ? (
          <p className="text-muted-foreground">Add a part or assembly.</p>
        ) : (
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 tabular-nums">
            <dt className="text-muted-foreground">Plates</dt>
            <dd className="text-right font-medium">{estimate.plates}</dd>
            <dt className="text-muted-foreground">Units</dt>
            <dd className="text-right font-medium">{estimate.units}</dd>
            <dt className="text-muted-foreground">Machine time</dt>
            <dd className="text-right font-medium">
              ≈ {formatMinutes(estimate.minutes)}
              {estimate.unestimated > 0 && '+'}
            </dd>
            <dt className="text-muted-foreground">Filament</dt>
            <dd className="text-right font-medium">
              ≈ {formatWeight(estimate.grams)}
            </dd>
          </dl>
        )}
        {unlimited > 0 && (
          <p className="text-xs text-muted-foreground">
            {pluralize(unlimited, 'unlimited line')} not counted: they fill idle
            printers one plate at a time and use the latest part file.
          </p>
        )}
        {estimate.unestimated > 0 && (
          <p className="text-xs text-muted-foreground">
            {pluralize(estimate.unestimated, 'part')} without a time estimate.
          </p>
        )}
        <RunSlicerReadiness partIds={partIds} />
      </CardContent>
    </Card>
  );
}

/** Page-first add (docs/UI_CONVENTIONS.md): details, the lines to make, and a live summary of what they queue. */
export function NewProductionRunPage() {
  const navigate = useNavigate();
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
  const printers =
    useOpenFarmApiEndpointsPrintersListFleetEndpoint().data?.data?.printers ??
    [];
  const catalog = useBomCatalog();
  const { ConfirmLeaveDialog, markClean } = useUnsavedChangesPrompt(
    form.formState.isDirty,
  );
  const create =
    useOpenFarmApiEndpointsProductionRunsCreateProductionRunEndpoint();

  async function onSubmit(values: FormValues) {
    try {
      const created = await create.mutateAsync({
        data: {
          ...runDetailsRequest(values),
          lines: values.lines.map((line) => ({
            targetKind: line.targetKind,
            targetId: line.targetId,
            quantity: line.unlimited ? null : Number(line.quantity),
            assignmentMode:
              line.printerId === AUTOMATIC ? 'Unassigned' : 'Pinned',
            pinnedPrinterId:
              line.printerId === AUTOMATIC ? null : line.printerId,
            ...(line.unlimited && {
              backfillPrinterCap: Number(line.cap),
              backfillCumulativeUnitCeiling:
                line.ceiling.trim() === '' ? null : Number(line.ceiling),
            }),
          })),
        },
      });
      toast.success('Run added.');
      invalidateProductionQueries(queryClient);
      markClean();
      navigate(`/production-runs/${created.data?.id}`);
    } catch (error) {
      toast.error(await extractErrorDetail(error, 'Could not add run.'));
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
          <div className="grid flex-1 items-start gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
            <div className="flex min-w-0 flex-col gap-6">
              <RunDetailsFields control={form.control} />
              <Card>
                <CardHeader>
                  <CardTitle>Lines</CardTitle>
                  <CardAction>
                    <TargetPicker
                      onPick={(target) =>
                        append({
                          targetKind: target.kind,
                          targetId: target.id,
                          targetName: target.name,
                          quantity:
                            target.kind === 'Part'
                              ? String(
                                  catalog.parts.find((p) => p.id === target.id)
                                    ?.unitsPerPrint ?? 1,
                                )
                              : '1',
                          unlimited: false,
                          printerId: AUTOMATIC,
                          cap: '1',
                          ceiling: '',
                        })
                      }
                    />
                  </CardAction>
                </CardHeader>
                <CardContent>
                  {fields.length === 0 ? (
                    <EmptyState
                      size="sm"
                      bordered={false}
                      title="No lines"
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
                            onRemove={() => remove(index)}
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
            </div>
            <Summary lines={lines} />
          </div>

          <FormFooter
            end={
              <>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => navigate('/production-runs')}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={create.isPending}>
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
