import {
  keepPreviousData,
  useMutation,
  useQueryClient,
} from '@tanstack/react-query';
import type { ColumnDef } from '@tanstack/react-table';
import { type RefObject, useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { DataTable } from '@/components/data-table/DataTable';
import { PermissionButton as Button } from '@/components/PermissionButton';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';
import { Switch } from '@/components/ui/switch';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import { useOpenFarmApiEndpointsPartsGetPartEndpoint } from '@/lib/api/generated/parts/parts';
import { useAuthorizedQuery as useQuery } from '@/lib/auth/useAuthorizedQuery';
import { formatMinutes } from '@/lib/formatMinutes';
import { pluralize } from '@/lib/pluralize';
import { invalidateProductionQueries } from '../hooks/useProductionHub';
import { PlanSummary } from './PlanSummary';
import {
  demandKey,
  type PlanLine,
  type PlanPart,
  previewPlan,
  type QuantityBasis,
  submitDemand,
} from './productionPlanner';
import { type RunTarget, TargetPicker, TargetThumbnail } from './TargetPicker';

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

type ComponentRow = PlanPart & {
  count: string;
  pending: boolean;
  setCount: (id: string, value: string) => void;
};

const componentColumns: ColumnDef<ComponentRow>[] = [
  { accessorKey: 'name', header: 'Part', enableSorting: false },
  {
    accessorKey: 'unitsPerPlate',
    header: 'Units per plate',
    enableSorting: false,
  },
  {
    id: 'plates',
    header: 'Plates',
    cell: ({ row: { original: part } }) => {
      const invalid = !whole(part.count, 0);
      return (
        <Field data-invalid={invalid}>
          <FieldLabel className="sr-only" htmlFor={`plates-${part.partId}`}>
            Plates for {part.name}
          </FieldLabel>
          <Input
            id={`plates-${part.partId}`}
            className="w-24"
            type="number"
            min={0}
            step={1}
            value={part.count}
            disabled={part.pending}
            aria-invalid={invalid}
            aria-describedby={invalid ? 'component-quantity-error' : undefined}
            onChange={(event) => part.setCount(part.partId, event.target.value)}
          />
        </Field>
      );
    },
  },
  {
    id: 'plannedUnits',
    header: 'Planned units',
    cell: ({ row: { original: part } }) =>
      whole(part.count, 0) ? Number(part.count) * part.unitsPerPlate : '—',
  },
];

function whole(value: string, min: number) {
  return (
    value.trim() !== '' &&
    Number.isSafeInteger(Number(value)) &&
    Number(value) >= min
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
  const [target, setTarget] = useState(presetTarget);
  const [quantity, setQuantity] = useState('1');
  const [basis, setBasis] = useState<QuantityBasis>(runId ? 'Units' : 'Plates');
  const [counts, setCounts] = useState<Record<string, string>>({});
  const [backfill, setBackfill] = useState(initialBackfill);
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
  const notReady =
    target?.kind === 'Part' &&
    partQuery.data?.data?.printRequirementsStatus !== 'Complete';
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

  async function submit() {
    if (!target || !valid || invalidPlan || pending) return;
    setError(null);
    const payload = {
      line,
      plateOverrides,
      planVersion: backfill ? undefined : plan.data?.version,
      ...(runId ? { productionRunId: runId } : {}),
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
            : `${target.name} sent to the queue.`,
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
      setError(await extractErrorDetail(failure, 'Could not send to queue.'));
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
    setError(null);
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !pending) onClose();
      }}
    >
      <DialogContent
        className={`max-h-[calc(100dvh-2rem)] overflow-y-auto ${target?.kind === 'Assembly' || runId ? 'sm:max-w-3xl' : 'sm:max-w-lg'}`}
        onCloseAutoFocus={(event) => {
          if (returnFocusRef?.current) {
            event.preventDefault();
            returnFocusRef.current.focus();
          }
        }}
      >
        <DialogHeader>
          <DialogTitle>
            {runId ? 'Add part or assembly' : 'Send to queue'}
          </DialogTitle>
          <DialogDescription>
            {runId
              ? 'Set the required output for this run. Jobs appear in Queue.'
              : 'Print full plates on matching printers.'}
          </DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            void submit();
          }}
        >
          <FieldGroup className="gap-4">
            {target ? (
              <div className="flex items-center gap-3">
                <TargetThumbnail kind={target.kind} />
                <span className="min-w-0 flex-1 truncate font-medium">
                  {target.name}
                </span>
                {!presetTarget && (
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    disabled={pending}
                    onClick={() => {
                      setTarget(undefined);
                      setCounts({});
                      setError(null);
                    }}
                  >
                    Change
                  </Button>
                )}
              </div>
            ) : (
              <TargetPicker onPick={pick} />
            )}
            {target && (
              <>
                {notReady && (
                  <p role="alert">
                    {partQuery.isPending
                      ? 'Loading part…'
                      : 'Print requirements incomplete. Complete them before queueing.'}
                  </p>
                )}
                {!backfill && !notReady && (
                  <>
                    {runId && target.kind === 'Part' && (
                      <Field>
                        <FieldLabel>Quantity basis</FieldLabel>
                        <ToggleGroup
                          type="single"
                          variant="outline"
                          value={basis}
                          disabled={pending}
                          onValueChange={(value) => {
                            if (value) setBasis(value as QuantityBasis);
                          }}
                        >
                          <ToggleGroupItem value="Units">
                            Unit target
                          </ToggleGroupItem>
                          <ToggleGroupItem value="Plates">
                            Plates
                          </ToggleGroupItem>
                        </ToggleGroup>
                      </Field>
                    )}
                    {componentPlates ? (
                      <>
                        <p className="text-sm text-muted-foreground">
                          Choose full plates for each printed component. Set a
                          count to zero to skip it.
                        </p>
                        {componentParts && (
                          <DataTable
                            data={componentParts.map((part) => ({
                              ...part,
                              count: counts[part.partId] ?? '1',
                              pending,
                              setCount: (id: string, value: string) =>
                                setCounts((previous) => ({
                                  ...previous,
                                  [id]: value,
                                })),
                            }))}
                            columns={componentColumns}
                            getRowId={(part) => part.partId}
                          />
                        )}
                        {!valid && (
                          <p
                            id="component-quantity-error"
                            role="alert"
                            className="text-sm text-destructive"
                          >
                            Use whole plate counts, zero or more.
                          </p>
                        )}
                      </>
                    ) : (
                      <Field data-invalid={!valid}>
                        <FieldLabel htmlFor="send-to-queue-quantity">
                          {target.kind === 'Assembly'
                            ? 'Quantity (sets)'
                            : basis === 'Units'
                              ? 'Quantity (units)'
                              : 'Number of plates'}
                        </FieldLabel>
                        <Input
                          id="send-to-queue-quantity"
                          className="w-28"
                          type="number"
                          min={1}
                          step={1}
                          value={quantity}
                          disabled={pending}
                          aria-invalid={!valid}
                          aria-describedby={
                            !valid ? 'queue-quantity-error' : undefined
                          }
                          onChange={(event) => setQuantity(event.target.value)}
                        />
                        {!valid && (
                          <p
                            id="queue-quantity-error"
                            className="text-sm text-destructive"
                          >
                            Whole number, 1 or more.
                          </p>
                        )}
                      </Field>
                    )}
                  </>
                )}
                {!runId && (
                  <Field orientation="horizontal">
                    <FieldLabel htmlFor="continuous-backfill">
                      Continuous backfill
                    </FieldLabel>
                    <Switch
                      id="continuous-backfill"
                      checked={backfill}
                      disabled={pending}
                      aria-describedby={
                        backfill ? 'backfill-description' : undefined
                      }
                      onCheckedChange={setBackfill}
                    />
                  </Field>
                )}
                {backfill && (
                  <p
                    id="backfill-description"
                    className="text-sm text-muted-foreground"
                  >
                    Prints when finite work leaves compatible capacity idle.
                    Starts with one printer at a time. Manage limits, pause, and
                    refresh the recipe from Queue.
                    {target.kind === 'Assembly' &&
                      ' Uses the whole assembly recipe to balance component output; finite plate counts and skipped components do not apply.'}
                  </p>
                )}
                {!notReady &&
                  (plan.isError ? (
                    <p role="alert" className="text-sm text-destructive">
                      {componentPlates &&
                      componentParts?.every(
                        (part) => Number(counts[part.partId] ?? 1) === 0,
                      )
                        ? 'Choose at least one plate.'
                        : 'Could not plan this print. Check plate counts and print requirements.'}{' '}
                      <Button
                        type="button"
                        variant="link"
                        disabled={pending || !valid}
                        onClick={() => plan.refetch()}
                      >
                        Retry
                      </Button>
                    </p>
                  ) : plan.isFetching ? (
                    <p role="status">Planning plates…</p>
                  ) : (
                    !backfill &&
                    valid &&
                    plan.data &&
                    (runId ? (
                      <PlanSummary plan={plan.data} />
                    ) : (
                      <p
                        className="text-sm text-muted-foreground tabular-nums"
                        aria-live="polite"
                      >
                        {pluralize(plan.data.plates, 'plate')} ·{' '}
                        {plan.data.parts.reduce(
                          (sum, part) => sum + part.plannedUnits,
                          0,
                        )}{' '}
                        planned units
                        {plan.data.completeSets != null &&
                          ` · ${pluralize(plan.data.completeSets, 'complete set')} from these printed components`}
                        {plan.data.minutes != null &&
                          ` · ≈ ${formatMinutes(plan.data.minutes)}`}
                      </p>
                    ))
                  ))}
              </>
            )}
            {error && (
              <p role="alert" className="text-sm text-destructive">
                {error}
              </p>
            )}
          </FieldGroup>
          <DialogFooter>
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
              disabled={!target || !valid || invalidPlan || pending}
            >
              {pending && <Spinner data-icon="inline-start" />}
              {pending
                ? 'Sending…'
                : backfill
                  ? 'Enable backfill'
                  : runId
                    ? 'Add to run'
                    : 'Send to queue'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
