import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query';
import { type RefObject, useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
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
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Spinner } from '@/components/ui/spinner';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { ListProductionRunsResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPartsGetPartEndpoint } from '@/lib/api/generated/parts/parts';
import { useOpenFarmApiEndpointsProductionRunsListProductionRunsEndpoint } from '@/lib/api/generated/production-runs/production-runs';
import { invalidateProductionQueries } from '../hooks/useProductionHub';
import { PlanSummary } from './PlanSummary';
import {
  demandKey,
  type PlanLine,
  previewPlan,
  type QuantityBasis,
  submitDemand,
} from './productionPlanner';
import { type RunTarget, TargetPicker, TargetThumbnail } from './TargetPicker';

const DIRECT = 'direct';
const NEW_RUN = 'new';

export function SendToQueueDialog({
  target: presetTarget,
  runId,
  onClose,
  returnFocusRef,
  initialBackfill = false,
}: {
  initialBackfill?: boolean;
  target?: RunTarget;
  runId?: string;
  onClose: () => void;
  returnFocusRef?: RefObject<HTMLButtonElement | null>;
}) {
  const navigate = useNavigate();
  const client = useQueryClient();
  const [target, setTarget] = useState(presetTarget);
  const [quantity, setQuantity] = useState('1');
  const [basis, setBasis] = useState<QuantityBasis>('Plates');
  const [run, setRun] = useState(runId ?? DIRECT);
  const [backfill, setBackfill] = useState(initialBackfill);
  const [error, setError] = useState<string | null>(null);
  const submission = useRef({ key: '', id: '' });
  const amount = Number(quantity);
  const valid = Number.isSafeInteger(amount) && amount >= 1;
  const line: PlanLine = {
    targetKind: target?.kind ?? 'Part',
    targetId: target?.id ?? '',
    quantity: backfill ? null : amount,
    quantityBasis: target?.kind === 'Assembly' ? 'Sets' : basis,
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
    queryKey: [demandKey, 'preview', line],
    queryFn: ({ signal }) => previewPlan(line, undefined, signal),
    placeholderData: keepPreviousData,
    enabled: !!target && valid && !notReady,
    retry: false,
  });
  const runsQuery =
    useOpenFarmApiEndpointsProductionRunsListProductionRunsEndpoint(
      { status: 'Open', page: 1, pageSize: 100 },
      { query: { enabled: !runId && !backfill } },
    );
  const send = useMutation({ mutationFn: submitDemand });
  const pending = send.isPending;
  const invalidPlan =
    notReady ||
    !plan.data ||
    plan.isFetching ||
    plan.isError ||
    (!backfill && plan.data.plates < 1) ||
    (run !== DIRECT && plan.data.parts.some((p) => p.surplusUnits < 0));

  async function submit() {
    if (!target || !valid || invalidPlan || pending) return;
    setError(null);
    const payload = {
      line,
      planVersion: backfill ? undefined : plan.data?.version,
      productionRunId: run !== DIRECT && run !== NEW_RUN ? run : undefined,
      createRun: run === NEW_RUN,
    };
    const key = JSON.stringify(payload);
    if (submission.current.key !== key)
      submission.current = { key, id: crypto.randomUUID() };
    try {
      const result = await send.mutateAsync({
        ...payload,
        submissionId: submission.current.id,
      });
      invalidateProductionQueries(client);
      toast.success(
        backfill
          ? `${target.name} backfill enabled.`
          : `${target.name} sent to the queue.`,
        {
          action: {
            label: result.productionRunId ? 'View run' : 'View queue',
            onClick: () =>
              navigate(
                result.productionRunId
                  ? `/production-runs/${result.productionRunId}`
                  : '/queue',
              ),
          },
        },
      );
      onClose();
    } catch (failure) {
      setError(await extractErrorDetail(failure, 'Could not send to queue.'));
      await plan.refetch();
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open && !pending) onClose();
      }}
    >
      <DialogContent
        className="max-h-[calc(100dvh-2rem)] overflow-y-auto sm:max-w-3xl"
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
            Print whole plates on matching printers. Track an order or batch in
            a Run when needed.
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
                    onClick={() => setTarget(undefined)}
                  >
                    Change
                  </Button>
                )}
              </div>
            ) : (
              <TargetPicker
                onPick={(picked) => {
                  setTarget(picked);
                  setQuantity('1');
                  setBasis('Plates');
                }}
              />
            )}
            {target && (
              <>
                {!runId && (
                  <Field>
                    <FieldLabel>Printing</FieldLabel>
                    <ToggleGroup
                      type="single"
                      variant="outline"
                      value={backfill ? 'backfill' : 'finite'}
                      disabled={pending}
                      onValueChange={(value) => {
                        if (value) {
                          setBackfill(value === 'backfill');
                          setRun(DIRECT);
                        }
                      }}
                    >
                      <ToggleGroupItem value="finite">
                        Fixed quantity
                      </ToggleGroupItem>
                      <ToggleGroupItem value="backfill">
                        Continuous backfill
                      </ToggleGroupItem>
                    </ToggleGroup>
                  </Field>
                )}
                {notReady && (
                  <p role="alert">
                    {partQuery.isPending
                      ? 'Loading part…'
                      : 'Print requirements incomplete. Complete them before queueing.'}
                  </p>
                )}
                {!backfill && !notReady && (
                  <>
                    {target.kind === 'Part' && (
                      <Field>
                        <FieldLabel>Quantity basis</FieldLabel>
                        <ToggleGroup
                          type="single"
                          variant="outline"
                          value={basis}
                          disabled={pending}
                          onValueChange={(value) => {
                            if (value) {
                              setBasis(value as QuantityBasis);
                            }
                          }}
                        >
                          <ToggleGroupItem value="Plates">
                            Plates
                          </ToggleGroupItem>
                          <ToggleGroupItem value="Units">
                            Unit target
                          </ToggleGroupItem>
                        </ToggleGroup>
                      </Field>
                    )}
                    <Field data-invalid={!valid}>
                      <FieldLabel htmlFor="send-to-queue-quantity">
                        {target.kind === 'Assembly'
                          ? 'Quantity (sets)'
                          : basis === 'Plates'
                            ? 'Quantity (plates)'
                            : 'Quantity (units)'}
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
                        onChange={(event) => {
                          setQuantity(event.target.value);
                        }}
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
                    {!runId && (
                      <Field>
                        <FieldLabel htmlFor="send-to-queue-run">
                          Track in a Run
                        </FieldLabel>
                        <Select
                          value={run}
                          disabled={pending}
                          onValueChange={(value) => {
                            setRun(value);
                          }}
                        >
                          <SelectTrigger id="send-to-queue-run">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectGroup>
                              <SelectItem value={DIRECT}>
                                No Run · send directly
                              </SelectItem>
                              <SelectItem value={NEW_RUN}>New Run</SelectItem>
                              {(
                                (
                                  runsQuery.data?.data as
                                    | ListProductionRunsResponse
                                    | undefined
                                )?.items ?? []
                              ).map((open) => (
                                <SelectItem key={open.id} value={open.id ?? ''}>
                                  {open.displayLabel}
                                </SelectItem>
                              ))}
                            </SelectGroup>
                          </SelectContent>
                        </Select>
                        {runsQuery.isError && (
                          <Button
                            type="button"
                            variant="link"
                            onClick={() => runsQuery.refetch()}
                          >
                            Retry loading Runs
                          </Button>
                        )}
                      </Field>
                    )}
                  </>
                )}
                {notReady ? null : backfill ? (
                  <p className="text-sm text-muted-foreground">
                    Prints when finite work leaves compatible capacity idle.
                    Starts with one printer at a time. Manage limits, pause, and
                    refresh the component recipe from Queue.
                  </p>
                ) : plan.isError ? (
                  <p role="alert" className="text-sm text-destructive">
                    Could not plan this print. Check print requirements and try
                    again.{' '}
                    <Button
                      type="button"
                      variant="link"
                      onClick={() => plan.refetch()}
                    >
                      Retry
                    </Button>
                  </p>
                ) : plan.isPending ? (
                  <p role="status">Planning plates…</p>
                ) : (
                  plan.data && <PlanSummary plan={plan.data} />
                )}
                {backfill && plan.isError && (
                  <p role="alert">
                    Print requirements incomplete or unavailable. Resolve them
                    before enabling backfill.
                  </p>
                )}
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
