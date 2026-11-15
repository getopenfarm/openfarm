import {
  keepPreviousData,
  useMutation,
  useQueryClient,
} from '@tanstack/react-query';
import { ComponentIcon, ImageOffIcon } from 'lucide-react';
import { type ReactNode, type RefObject, useId, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { toast } from 'sonner';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Badge } from '@/components/ui/badge';
import {
  Dialog,
  DialogScrollableContent,
  DialogTitle,
} from '@/components/ui/dialog';
import { Spinner } from '@/components/ui/spinner';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import {
  type FilamentChoice,
  PartFilamentRequirementsList,
} from '@/features/parts/components/PartFilamentRequirementsList';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { PartFilamentRequirementResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPartsGetPartEndpoint } from '@/lib/api/generated/parts/parts';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { useAuthorizedQuery as useQuery } from '@/lib/auth/useAuthorizedQuery';
import { formatMinutes } from '@/lib/formatMinutes';
import { formatWeight } from '@/lib/formatUnits';
import { pluralize } from '@/lib/pluralize';
import { formatEstimateMetric } from '@/lib/printEstimates';
import { cn } from '@/lib/utils';
import { invalidateProductionQueries } from '../hooks/useProductionHub';
import { PlanSummary } from './PlanSummary';
import { PlateStepper } from './PlateStepper';
import { AUTOMATIC, PrinterCombobox } from './PrinterCombobox';
import {
  demandKey,
  type PlanLine,
  previewPlan,
  type QuantityBasis,
  submitDemand,
} from './productionPlanner';
import { type RunTarget, TargetPicker } from './TargetPicker';

type DialogProps = {
  target?: RunTarget;
  onClose: () => void;
  returnFocusRef?: RefObject<HTMLButtonElement | null>;
};

export function SendToQueueDialog(
  props: DialogProps & { initialBackfill?: boolean },
) {
  return <DemandDialog {...props} />;
}

export function AddToRunDialog(props: DialogProps & { runId: string }) {
  return <DemandDialog {...props} />;
}

/** Whether a choice is what the part already prints with, so it needs no override. */
function partsOwnChoice(
  requirement: PartFilamentRequirementResponse,
  choice: FilamentChoice,
) {
  return (
    !requirement.narrowFilamentProfileId &&
    (requirement.narrowFilamentVariantId ?? null) === choice.variantId &&
    (requirement.narrowFilamentId ?? null) === choice.filamentId &&
    (requirement.anyColor ?? false) === choice.anyColor
  );
}

function whole(value: string, min: number) {
  return (
    value.trim() !== '' &&
    Number.isSafeInteger(Number(value)) &&
    Number(value) >= min
  );
}

/** The thing being printed: its plate, stacked once per extra plate, with the count. */
function PlateCard({
  kind,
  thumbnail,
  plates,
  children,
}: {
  kind: RunTarget['kind'];
  thumbnail?: string | null;
  plates: number;
  children: ReactNode;
}) {
  const sheets = Math.min(Math.max(plates - 1, 0), 2);
  return (
    <div className="flex gap-4">
      <div className="relative mt-3 mr-3 size-22 shrink-0" aria-hidden>
        {[2, 1].map((layer) => (
          <div
            key={layer}
            className="absolute inset-0 rounded-lg border border-foreground/15 bg-background motion-safe:transition-[transform,opacity] motion-safe:duration-150"
            style={
              layer <= sheets
                ? { transform: `translate(${layer * 6}px, ${-layer * 6}px)` }
                : { opacity: 0 }
            }
          />
        ))}
        <div className="relative flex size-full items-center justify-center overflow-hidden rounded-lg border border-foreground/15 bg-muted text-muted-foreground">
          {kind === 'Assembly' ? (
            <ComponentIcon className="size-6" />
          ) : thumbnail ? (
            <img
              src={`data:image/png;base64,${thumbnail}`}
              alt=""
              className="size-full object-contain"
            />
          ) : (
            <ImageOffIcon className="size-6" />
          )}
        </div>
        {plates > 1 && (
          <Badge className="absolute -right-2.5 -bottom-2 tabular-nums">
            ×{plates}
          </Badge>
        )}
      </div>
      <div className="flex min-w-0 flex-1 flex-col items-start gap-2.5">
        {children}
      </div>
    </div>
  );
}

/** One line of the summary: a label column and its value. */
function Row({
  label,
  htmlFor,
  labelId,
  hint,
  children,
}: {
  label: string;
  htmlFor?: string;
  labelId?: string;
  hint?: string;
  children: ReactNode;
}) {
  const labelClass = 'text-sm text-muted-foreground sm:leading-8';
  return (
    <div className="grid gap-1 py-2 sm:grid-cols-[5rem_minmax(0,1fr)] sm:gap-x-3">
      {htmlFor ? (
        <label htmlFor={htmlFor} className={labelClass}>
          {label}
        </label>
      ) : (
        <span id={labelId} className={labelClass}>
          {label}
        </span>
      )}
      <div className="min-w-0">{children}</div>
      {hint && (
        <p className="text-xs text-muted-foreground sm:col-start-2">{hint}</p>
      )}
    </div>
  );
}

function DemandDialog({
  target: presetTarget,
  runId,
  onClose,
  returnFocusRef,
  initialBackfill = false,
}: DialogProps & { runId?: string; initialBackfill?: boolean }) {
  const navigate = useNavigate();
  const client = useQueryClient();
  const ids = useId();
  const [target, setTarget] = useState(presetTarget);
  const [quantity, setQuantity] = useState('1');
  const [basis, setBasis] = useState<QuantityBasis>(runId ? 'Units' : 'Plates');
  const [counts, setCounts] = useState<Record<string, string>>({});
  const [backfill, setBackfill] = useState(initialBackfill);
  const [printerId, setPrinterId] = useState(AUTOMATIC);
  const [placeAtTop, setPlaceAtTop] = useState(false);
  const [choices, setChoices] = useState<Record<string, FilamentChoice>>({});
  const [error, setError] = useState<string | null>(null);
  const submission = useRef({ key: '', id: '' });
  const componentPlates = !runId && !backfill && target?.kind === 'Assembly';
  const valid =
    backfill ||
    (componentPlates
      ? Object.values(counts).every((value) => whole(value, 0))
      : whole(quantity, 1));
  const plateOverrides = componentPlates
    ? Object.fromEntries(
        Object.entries(counts).map(([id, count]) => [id, Number(count)]),
      )
    : undefined;
  const line: PlanLine = {
    targetKind: target?.kind ?? 'Part',
    targetId: target?.id ?? '',
    quantity: backfill ? null : componentPlates ? 1 : Number(quantity),
    quantityBasis:
      target?.kind === 'Assembly'
        ? componentPlates
          ? 'Plates'
          : 'Sets'
        : basis,
    assignmentMode: 'Unassigned',
  };
  const partQuery = useOpenFarmApiEndpointsPartsGetPartEndpoint(
    target?.id ?? '',
    {
      query: {
        enabled: target?.kind === 'Part',
        staleTime: 0,
        refetchOnMount: 'always',
      },
    },
  );
  const part = target?.kind === 'Part' ? partQuery.data?.data : undefined;
  const notReady =
    target?.kind === 'Part' && part?.printRequirementsStatus !== 'Complete';
  const printers =
    useOpenFarmApiEndpointsPrintersListFleetEndpoint().data?.data?.printers ??
    [];
  const filamentRequirements =
    !backfill && !notReady ? (part?.filamentRequirements ?? []) : [];
  const plan = useQuery({
    queryKey: [demandKey, 'preview', line, plateOverrides],
    queryFn: ({ signal }) => previewPlan(line, plateOverrides, signal),
    placeholderData: (previous, previousQuery) =>
      (previousQuery?.queryKey[2] as PlanLine | undefined)?.targetId ===
      target?.id
        ? keepPreviousData(previous)
        : undefined,
    enabled: !!target && valid && !notReady,
    retry: false,
  });
  // Retain the component form even when an edited plan is invalid (for example all zeros).
  const assemblyLine: PlanLine = {
    ...line,
    quantity: 1,
    quantityBasis: 'Plates',
  };
  const assemblyPlan = useQuery({
    queryKey: [demandKey, 'preview', assemblyLine, {}],
    queryFn: ({ signal }) => previewPlan(assemblyLine, {}, signal),
    enabled: componentPlates,
    retry: false,
  });
  const componentParts = plan.data?.parts ?? assemblyPlan.data?.parts;
  const send = useMutation({ mutationFn: submitDemand });
  const pending = send.isPending;
  const invalidPlan =
    notReady ||
    !plan.data ||
    plan.isFetching ||
    plan.isPlaceholderData ||
    plan.isError ||
    (!backfill && plan.data.plates < 1);
  // Follows the controls at once; the preview catches up for unit and set targets.
  const plates = componentPlates
    ? (componentParts ?? []).reduce((sum, item) => {
        const count = counts[item.partId] ?? '1';
        return sum + (whole(count, 0) ? Number(count) : 0);
      }, 0)
    : target?.kind === 'Part' && basis === 'Plates'
      ? whole(quantity, 1)
        ? Number(quantity)
        : 1
      : (plan.data?.plates ?? 1);
  const selectedPrinter = printers.find((printer) => printer.id === printerId);

  async function submit() {
    if (!target || !valid || invalidPlan || pending) return;
    setError(null);
    const filamentChoices = filamentRequirements.flatMap(({ id }) => {
      const choice = id ? choices[id] : undefined;
      return id && choice
        ? [
            {
              requirementId: id,
              narrowFilamentVariantId: choice.variantId,
              narrowFilamentId: choice.filamentId,
              anyColor: choice.anyColor,
            },
          ]
        : [];
    });
    const payload = {
      // The preview ignores the printer, so only the submitted line carries it.
      line:
        printerId === AUTOMATIC
          ? line
          : { ...line, assignmentMode: 'Pinned', pinnedPrinterId: printerId },
      plateOverrides,
      planVersion: backfill ? undefined : plan.data?.version,
      ...(runId ? { productionRunId: runId } : {}),
      ...(!backfill && placeAtTop ? { placeAtTop } : {}),
      ...(filamentChoices.length > 0 ? { filamentChoices } : {}),
    };
    const key = JSON.stringify(payload);
    if (submission.current.key !== key)
      submission.current = { key, id: crypto.randomUUID() };
    try {
      await send.mutateAsync({
        ...payload,
        submissionId: submission.current.id,
      });
      invalidateProductionQueries(client);
      toast.success(
        backfill
          ? `${target.name} backfill enabled.`
          : runId
            ? `${target.name} added to the run.`
            : `Queued ${pluralize(plates, 'plate')} of ${target.name}.`,
        {
          action: {
            label: runId ? 'View run' : 'View queue',
            onClick: () =>
              navigate(runId ? `/production-runs/${runId}` : '/queue'),
          },
        },
      );
      onClose();
    } catch (failure) {
      setError(await extractErrorDetail(failure, 'Send failed.'));
      if (componentPlates) {
        const refreshed = await assemblyPlan.refetch();
        if (refreshed.data) {
          const ids = new Set(refreshed.data.parts.map((part) => part.partId));
          setCounts((previous) =>
            Object.fromEntries(
              Object.entries(previous).filter(([id]) => ids.has(id)),
            ),
          );
        }
      }
      await plan.refetch();
    }
  }

  function pick(picked: RunTarget) {
    setTarget(picked);
    setQuantity('1');
    setBasis(runId ? 'Units' : 'Plates');
    setCounts({});
    setChoices({});
    setPrinterId(AUTOMATIC);
    setError(null);
  }

  const perPlate = part
    ? [
        `${part.unitsPerPrint ?? 1} per plate`,
        formatEstimateMetric(part.printEstimate?.time, 'time'),
        formatEstimateMetric(part.printEstimate?.filament, 'filament'),
      ].filter((fact) => fact !== 'Unavailable')
    : target?.kind === 'Assembly' && componentParts
      ? [pluralize(componentParts.length, 'printed component')]
      : [];
  const units = plan.data?.parts.reduce(
    (sum, item) => sum + item.plannedUnits,
    0,
  );
  const readout =
    !target || notReady || backfill || runId ? null : plan.isError ? (
      <p role="alert" className="text-sm text-destructive">
        {componentPlates &&
        componentParts?.every((item) => Number(counts[item.partId] ?? 1) === 0)
          ? 'Choose at least 1 plate.'
          : 'Planning failed. Check plates, print requirements.'}{' '}
        <Button
          type="button"
          variant="link"
          className="h-auto p-0"
          disabled={pending || !valid}
          onClick={() => plan.refetch()}
        >
          Retry
        </Button>
      </p>
    ) : plan.data && valid ? (
      <p
        aria-live="polite"
        className={cn(
          'text-sm tabular-nums',
          plan.isFetching && 'text-muted-foreground',
        )}
      >
        {pluralize(units ?? 0, 'unit')}
        {plan.data.completeSets != null &&
          ` · ${pluralize(plan.data.completeSets, 'complete set')}`}
        {plan.data.minutes != null &&
          ` · ≈ ${formatMinutes(plan.data.minutes)}`}
        {plan.data.grams != null && ` · ${formatWeight(plan.data.grams)}`}
      </p>
    ) : plan.isFetching ? (
      <p role="status" className="text-sm text-muted-foreground">
        Planning…
      </p>
    ) : null;
  const action = backfill
    ? { idle: 'Enable backfill', busy: 'Enabling…' }
    : runId
      ? { idle: 'Add to run', busy: 'Adding…' }
      : { idle: `Queue ${pluralize(plates, 'plate')}`, busy: 'Queuing…' };
  const stepperLabel =
    target?.kind === 'Assembly'
      ? 'Sets'
      : basis === 'Units'
        ? 'Units'
        : 'Plates';

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !pending) onClose();
      }}
    >
      <DialogScrollableContent
        className={runId ? 'sm:max-w-3xl' : 'sm:max-w-xl'}
        aria-describedby={undefined}
        onCloseAutoFocus={(event) => {
          if (returnFocusRef?.current) {
            event.preventDefault();
            returnFocusRef.current.focus();
          }
        }}
        header={
          <DialogTitle className="truncate pr-6">
            {runId
              ? target
                ? `Add ${target.name}`
                : 'Add part or assembly'
              : target
                ? `Print ${target.name}`
                : 'Print'}
          </DialogTitle>
        }
        footer={
          <div className="flex w-full flex-col gap-3 sm:flex-row sm:items-center">
            <div className="min-w-0 sm:mr-auto">
              {error ? (
                <p role="alert" className="text-sm text-destructive">
                  {error}
                </p>
              ) : (
                readout
              )}
            </div>
            <div className="flex justify-end gap-2">
              <Button
                type="button"
                variant="outline"
                disabled={pending}
                onClick={onClose}
              >
                Cancel
              </Button>
              <Button
                permission="production-runs:create"
                type="submit"
                form={`${ids}-form`}
                disabled={!target || !valid || invalidPlan || pending}
              >
                {pending && <Spinner data-icon="inline-start" />}
                {pending ? action.busy : action.idle}
              </Button>
            </div>
          </div>
        }
      >
        <form
          id={`${ids}-form`}
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            void submit();
          }}
        >
          {!target ? (
            <TargetPicker onPick={pick} />
          ) : (
            <>
              <PlateCard
                kind={target.kind}
                thumbnail={part?.thumbnails?.[0]?.thumbnailBase64}
                plates={backfill ? 1 : plates}
              >
                <div className="flex w-full items-center gap-2">
                  <p className="flex min-w-0 flex-1 flex-wrap gap-x-3 text-sm text-muted-foreground tabular-nums">
                    {perPlate.map((fact) => (
                      <span key={fact}>{fact}</span>
                    ))}
                  </p>
                  {!presetTarget && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      disabled={pending}
                      onClick={() => {
                        setTarget(undefined);
                        setCounts({});
                        setChoices({});
                        setError(null);
                      }}
                    >
                      Change
                    </Button>
                  )}
                </div>
                {notReady ? (
                  <p role="alert" className="text-sm">
                    {partQuery.isPending ? (
                      'Loading part…'
                    ) : (
                      <>
                        Print requirements incomplete.{' '}
                        <Link
                          to={`/parts/${target.id}`}
                          className="text-primary underline-offset-4 hover:underline"
                        >
                          Open part
                        </Link>
                      </>
                    )}
                  </p>
                ) : (
                  <>
                    {(!runId || target.kind === 'Part') && (
                      <ToggleGroup
                        type="single"
                        variant="outline"
                        size="sm"
                        spacing={0}
                        aria-label={runId ? 'Quantity basis' : 'Quantity'}
                        value={
                          runId ? basis : backfill ? 'Continuous' : 'Plates'
                        }
                        disabled={pending}
                        onValueChange={(value) => {
                          if (!value) return;
                          if (runId) setBasis(value as QuantityBasis);
                          else setBackfill(value === 'Continuous');
                        }}
                      >
                        {runId && (
                          <ToggleGroupItem value="Units">Units</ToggleGroupItem>
                        )}
                        <ToggleGroupItem value="Plates">Plates</ToggleGroupItem>
                        {!runId && (
                          <ToggleGroupItem value="Continuous">
                            Continuous
                          </ToggleGroupItem>
                        )}
                      </ToggleGroup>
                    )}
                    {backfill ? (
                      <p className="text-sm text-muted-foreground">
                        Prints when a matching printer is idle. Limits in Queue.
                        {target.kind === 'Assembly' &&
                          ' Uses the assembly recipe. Component plates do not apply.'}
                      </p>
                    ) : (
                      !componentPlates && (
                        <div className="flex flex-col gap-1">
                          <PlateStepper
                            id={`${ids}-quantity`}
                            label={stepperLabel}
                            value={quantity}
                            onChange={setQuantity}
                            disabled={pending}
                            invalid={!valid}
                            describedBy={
                              !valid ? `${ids}-quantity-error` : undefined
                            }
                          />
                          {!valid && (
                            <p
                              id={`${ids}-quantity-error`}
                              className="text-sm text-destructive"
                            >
                              Whole number, 1 or more.
                            </p>
                          )}
                        </div>
                      )
                    )}
                  </>
                )}
              </PlateCard>
              {componentPlates && componentParts && (
                <div className="flex flex-col gap-1">
                  <ul aria-label="Components" className="divide-y border-t">
                    {componentParts.map((item) => {
                      const count = counts[item.partId] ?? '1';
                      return (
                        <li
                          key={item.partId}
                          className="flex items-center gap-3 py-2"
                        >
                          <span className="flex min-w-0 flex-1 flex-col">
                            <span className="truncate text-sm">
                              {item.name}
                            </span>
                            <span className="text-xs text-muted-foreground tabular-nums">
                              {item.unitsPerPlate} per plate
                            </span>
                          </span>
                          <PlateStepper
                            label={`Plates for ${item.name}`}
                            min={0}
                            value={count}
                            disabled={pending}
                            invalid={!whole(count, 0)}
                            onChange={(value) =>
                              setCounts((previous) => ({
                                ...previous,
                                [item.partId]: value,
                              }))
                            }
                          />
                          <span className="w-16 text-right text-sm text-muted-foreground tabular-nums">
                            {whole(count, 0)
                              ? pluralize(
                                  Number(count) * item.unitsPerPlate,
                                  'unit',
                                )
                              : '—'}
                          </span>
                        </li>
                      );
                    })}
                  </ul>
                  {!valid && (
                    <p role="alert" className="text-sm text-destructive">
                      Whole plate counts, 0 or more.
                    </p>
                  )}
                </div>
              )}
              {!notReady && (
                <div className="divide-y border-t">
                  <Row
                    label="Printer"
                    htmlFor={`${ids}-printer`}
                    hint={
                      selectedPrinter
                        ? 'Jobs print only on this printer.'
                        : undefined
                    }
                  >
                    <PrinterCombobox
                      id={`${ids}-printer`}
                      printers={printers}
                      value={printerId}
                      onChange={setPrinterId}
                      disabled={pending}
                    />
                  </Row>
                  {!backfill && (
                    <Row label="Position" labelId={`${ids}-position`}>
                      <ToggleGroup
                        type="single"
                        variant="outline"
                        size="sm"
                        spacing={0}
                        aria-labelledby={`${ids}-position`}
                        value={placeAtTop ? 'Top' : 'Bottom'}
                        disabled={pending}
                        onValueChange={(value) => {
                          if (value) setPlaceAtTop(value === 'Top');
                        }}
                      >
                        <ToggleGroupItem value="Bottom">Bottom</ToggleGroupItem>
                        <ToggleGroupItem value="Top">Top</ToggleGroupItem>
                      </ToggleGroup>
                    </Row>
                  )}
                  {filamentRequirements.length > 0 && (
                    <Row label="Filament" labelId={`${ids}-filament`}>
                      <fieldset
                        aria-labelledby={`${ids}-filament`}
                        disabled={pending}
                        className="min-w-0"
                      >
                        <PartFilamentRequirementsList
                          layout="row"
                          requirements={filamentRequirements.map(
                            (requirement) => {
                              const choice = requirement.id
                                ? choices[requirement.id]
                                : undefined;
                              return {
                                ...requirement,
                                key: requirement.id ?? '',
                                materialType: requirement.materialType,
                                detectedColorHex: requirement.detectedColorHex,
                                detectedSubBrand: requirement.detectedSubBrand,
                                overridden: !!choice,
                                ...(choice && {
                                  narrowFilamentVariantId: choice.variantId,
                                  narrowFilamentId: choice.filamentId,
                                  anyColor: choice.anyColor,
                                  narrowFilamentProfileId: null,
                                }),
                              };
                            },
                          )}
                          onNarrow={(key, choice) => {
                            const requirement = filamentRequirements.find(
                              (r) => r.id === key,
                            );
                            if (!requirement) return;
                            setChoices(({ [key]: _, ...rest }) =>
                              partsOwnChoice(requirement, choice)
                                ? rest
                                : { ...rest, [key]: choice },
                            );
                          }}
                          onReset={(key) =>
                            setChoices(({ [key]: _, ...rest }) => rest)
                          }
                        />
                      </fieldset>
                    </Row>
                  )}
                </div>
              )}
              {runId && !backfill && valid && plan.data && (
                <PlanSummary plan={plan.data} />
              )}
            </>
          )}
        </form>
      </DialogScrollableContent>
    </Dialog>
  );
}
