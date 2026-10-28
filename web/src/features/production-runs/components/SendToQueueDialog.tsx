import { useQueryClient } from '@tanstack/react-query';
import { type RefObject, useState } from 'react';
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
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type {
  ListProductionRunsResponse,
  PartResponse,
  ProductionRunDetailResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPartsGetPartEndpoint } from '@/lib/api/generated/parts/parts';
import {
  useOpenFarmApiEndpointsProductionRunsAddRunLinesEndpoint,
  useOpenFarmApiEndpointsProductionRunsCreateProductionRunEndpoint,
  useOpenFarmApiEndpointsProductionRunsListProductionRunsEndpoint,
} from '@/lib/api/generated/production-runs/production-runs';
import { formatMinutes } from '@/lib/formatMinutes';
import { pluralize } from '@/lib/pluralize';
import { invalidateProductionQueries } from '../hooks/useProductionHub';
import { type RunTarget, TargetPicker, TargetThumbnail } from './TargetPicker';
import { useRunEstimate } from './useRunEstimate';

const NEW_RUN = 'new';

/**
 * Queues a part or an assembly: into a new run (the default) or onto an open one. From a run page the run is fixed and
 * the target is picked here. The readout says what the quantity turns into before anything is queued.
 */
export function SendToQueueDialog({
  target: presetTarget,
  runId,
  onClose,
  returnFocusRef,
}: {
  target?: RunTarget;
  runId?: string;
  onClose: () => void;
  returnFocusRef?: RefObject<HTMLButtonElement | null>;
}) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [target, setTarget] = useState(presetTarget);
  // null until edited: the field shows one plate's worth (or one set) and keeps whatever the operator types, even blank.
  const [quantity, setQuantity] = useState<string | null>(null);
  const [run, setRun] = useState(runId ?? NEW_RUN);
  const [error, setError] = useState<string | null>(null);

  // Fresh on every open: a list row can be stale, and an incomplete part must not get a queue form.
  const partQuery = useOpenFarmApiEndpointsPartsGetPartEndpoint(
    target?.kind === 'Part' ? target.id : '',
    {
      query: {
        enabled: target?.kind === 'Part',
        staleTime: 0,
        refetchOnMount: 'always',
        refetchOnWindowFocus: false,
      },
    },
  );
  const part =
    target?.kind === 'Part'
      ? (partQuery.data?.data as PartResponse | undefined)
      : undefined;
  const loadingPart = target?.kind === 'Part' && partQuery.isPending;
  const perPlate = part?.unitsPerPrint ?? 1;
  const notReady =
    part !== undefined && part.printRequirementsStatus !== 'Complete';
  const shownQuantity =
    quantity ?? String(target?.kind === 'Part' ? perPlate : 1);
  const amount = Number(shownQuantity);
  const valid = Number.isInteger(amount) && amount >= 1;
  const estimate = useRunEstimate(
    target
      ? [
          {
            targetKind: target.kind,
            targetId: target.id,
            quantity: valid ? amount : 0,
          },
        ]
      : [],
  );

  const openRuns = (
    useOpenFarmApiEndpointsProductionRunsListProductionRunsEndpoint(
      { status: 'Open', page: 1, pageSize: 100 },
      { query: { enabled: !runId } },
    ).data?.data as ListProductionRunsResponse | undefined
  )?.items;

  const create =
    useOpenFarmApiEndpointsProductionRunsCreateProductionRunEndpoint();
  const addLines = useOpenFarmApiEndpointsProductionRunsAddRunLinesEndpoint();
  const pending = create.isPending || addLines.isPending;

  async function submit() {
    if (!target || !valid || pending) return;
    setError(null);
    const line = {
      targetKind: target.kind,
      targetId: target.id,
      quantity: amount,
      assignmentMode: 'Unassigned',
    };
    try {
      const response =
        run === NEW_RUN
          ? await create.mutateAsync({ data: { lines: [line] } })
          : await addLines.mutateAsync({ id: run, data: { lines: [line] } });
      const queuedRunId = (
        response.data as ProductionRunDetailResponse | undefined
      )?.id;
      invalidateProductionQueries(queryClient);
      toast.success(`${target.name} sent to the queue.`, {
        action:
          queuedRunId && !runId
            ? {
                label: 'View run',
                onClick: () => navigate(`/production-runs/${queuedRunId}`),
              }
            : undefined,
      });
      onClose();
    } catch (failure) {
      setError(await extractErrorDetail(failure, 'Could not send to queue.'));
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
            Plates go to a matching printer automatically.
          </DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            submit();
          }}
        >
          <FieldGroup className="gap-4">
            {target ? (
              <div className="flex items-center gap-3">
                <TargetThumbnail
                  kind={target.kind}
                  thumbnailBase64={part?.thumbnails?.[0]?.thumbnailBase64}
                />
                <div className="flex min-w-0 flex-1 flex-col">
                  <span className="truncate font-medium">{target.name}</span>
                  <span className="text-xs text-muted-foreground">
                    {target.kind === 'Assembly'
                      ? 'Assembly'
                      : `${perPlate} per plate`}
                  </span>
                </div>
                {!presetTarget && (
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
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
                  setQuantity(null);
                }}
              />
            )}

            {loadingPart ? (
              <p role="status" className="text-sm text-muted-foreground">
                Loading part…
              </p>
            ) : partQuery.isError ? (
              <p role="alert" className="text-sm text-destructive">
                Could not load part. Close and try again.
              </p>
            ) : notReady ? (
              <p role="alert" className="text-sm text-destructive">
                Print requirements incomplete. Complete them before queueing.
              </p>
            ) : (
              target && (
                <>
                  <Field>
                    <FieldLabel htmlFor="send-to-queue-quantity">
                      {target.kind === 'Assembly'
                        ? 'Quantity (sets)'
                        : 'Quantity (units)'}
                    </FieldLabel>
                    <Input
                      id="send-to-queue-quantity"
                      type="number"
                      min={1}
                      step={1}
                      value={shownQuantity}
                      disabled={pending}
                      aria-invalid={!valid}
                      aria-describedby={
                        valid ? undefined : 'send-to-queue-quantity-error'
                      }
                      onChange={(event) => setQuantity(event.target.value)}
                    />
                    {!valid && (
                      <p
                        id="send-to-queue-quantity-error"
                        className="text-sm text-destructive"
                      >
                        Whole number, 1 or more.
                      </p>
                    )}
                  </Field>
                  {!runId && (
                    <Field>
                      <FieldLabel htmlFor="send-to-queue-run">Run</FieldLabel>
                      <Select value={run} onValueChange={setRun}>
                        <SelectTrigger
                          id="send-to-queue-run"
                          className="w-full"
                        >
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectGroup>
                            <SelectItem value={NEW_RUN}>New run</SelectItem>
                            {(openRuns ?? []).map((open) => (
                              <SelectItem key={open.id} value={open.id ?? ''}>
                                {open.displayLabel}
                              </SelectItem>
                            ))}
                          </SelectGroup>
                        </SelectContent>
                      </Select>
                    </Field>
                  )}
                  <p
                    className="text-sm text-muted-foreground tabular-nums"
                    aria-live="polite"
                  >
                    {valid && !estimate.isPending && estimate.plates > 0
                      ? [
                          pluralize(estimate.plates, 'plate'),
                          target.kind === 'Part'
                            ? `${estimate.units} units`
                            : undefined,
                          estimate.unestimated === 0
                            ? `≈ ${formatMinutes(estimate.minutes)}`
                            : undefined,
                        ]
                          .filter(Boolean)
                          .join(' · ')
                      : ' '}
                  </p>
                </>
              )
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
              disabled={!target || !valid || notReady || loadingPart || pending}
            >
              {pending && <Spinner data-icon="inline-start" />}
              {pending ? 'Sending…' : runId ? 'Add to run' : 'Send to queue'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
