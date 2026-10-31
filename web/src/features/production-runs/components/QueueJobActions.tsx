import { useQueryClient } from '@tanstack/react-query';
import {
  ArrowUpToLineIcon,
  PinIcon,
  PinOffIcon,
  Trash2Icon,
} from 'lucide-react';
import { useRef, useState } from 'react';
import { toast } from 'sonner';
import { DataTableRowActions } from '@/components/data-table/DataTableRowActions';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
} from '@/components/ui/dropdown-menu';
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
  FleetPrinterResponse,
  QueueJobRowResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintJobsListQueueJobsEndpointQueryKey,
  useOpenFarmApiEndpointsPrintJobsPinPrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsRequeuePrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsUnpinPrintJobEndpoint,
} from '@/lib/api/generated/print-jobs/print-jobs';
import { invalidateProductionQueries } from '../hooks/useProductionHub';
import { JobRemoteControls } from './JobRemoteControls';
import { RemoveJobButton } from './RemoveJobButton';

export type RemoteAction = 'pause' | 'resume' | 'cancel' | null;

export function QueueJobActions({
  job,
  printers,
  onMoveToTop,
  pending,
  onPendingChange,
}: {
  job: QueueJobRowResponse;
  printers: FleetPrinterResponse[];
  onMoveToTop?: () => void;
  pending: RemoteAction;
  onPendingChange: (action: RemoteAction) => void;
}) {
  const triggerRef = useRef<HTMLButtonElement>(null);
  const queryClient = useQueryClient();
  const [pinOpen, setPinOpen] = useState(false);
  const [removeOpen, setRemoveOpen] = useState(false);
  const [printerId, setPrinterId] = useState('');
  const invalidate = () =>
    queryClient.invalidateQueries({
      queryKey: getOpenFarmApiEndpointsPrintJobsListQueueJobsEndpointQueryKey(),
    });
  const pin = useOpenFarmApiEndpointsPrintJobsPinPrintJobEndpoint({
    mutation: {
      onSuccess: () => {
        setPinOpen(false);
        setPrinterId('');
        toast.success('Job pinned.');
        invalidate();
      },
      onError: async (error) =>
        toast.error(await extractErrorDetail(error, 'Could not pin job.')),
    },
  });
  const unpin = useOpenFarmApiEndpointsPrintJobsUnpinPrintJobEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Job unpinned.');
        invalidate();
      },
      onError: async (error) =>
        toast.error(await extractErrorDetail(error, 'Could not unpin job.')),
    },
  });
  const requeue = useOpenFarmApiEndpointsPrintJobsRequeuePrintJobEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Plate requeued.');
        invalidateProductionQueries(queryClient);
      },
      onError: async (error) =>
        toast.error(
          await extractErrorDetail(error, 'Could not requeue plate.'),
        ),
    },
  });
  const jobId = job.jobId;
  if (!jobId) return null;
  const waiting = job.status === 'Queued' && !job.dispatchedPrinterId;
  if (
    !waiting &&
    job.status !== 'Printing' &&
    job.status !== 'Paused' &&
    !job.canRequeue
  )
    return null;
  return (
    <>
      <DataTableRowActions
        label={job.partName ?? 'job'}
        triggerRef={triggerRef}
        disabled={requeue.isPending || pin.isPending || unpin.isPending}
      >
        <DropdownMenuGroup>
          {job.canRequeue && (
            <DropdownMenuItem
              disabled={requeue.isPending}
              onSelect={() => requeue.mutate({ id: jobId })}
            >
              {job.targetUnits != null
                ? 'Requeue missing units'
                : 'Requeue plate'}
            </DropdownMenuItem>
          )}
          {waiting && (
            <>
              {job.assignmentMode === 'Pinned' ? (
                <DropdownMenuItem
                  disabled={unpin.isPending}
                  onSelect={() => unpin.mutate({ id: jobId })}
                >
                  <PinOffIcon />
                  Unpin
                </DropdownMenuItem>
              ) : (
                <DropdownMenuItem onSelect={() => setPinOpen(true)}>
                  <PinIcon />
                  Pin to printer
                </DropdownMenuItem>
              )}
              {onMoveToTop && (
                <DropdownMenuItem onSelect={onMoveToTop}>
                  <ArrowUpToLineIcon />
                  Move to top
                </DropdownMenuItem>
              )}
            </>
          )}
        </DropdownMenuGroup>
        {waiting && <DropdownMenuSeparator />}
        <DropdownMenuGroup>
          {waiting && (
            <DropdownMenuItem
              variant="destructive"
              onSelect={() => setRemoveOpen(true)}
            >
              <Trash2Icon />
              Remove job
            </DropdownMenuItem>
          )}
          <JobRemoteControls
            jobId={jobId}
            status={job.status ?? ''}
            menu
            pending={pending}
            onPendingChange={onPendingChange}
          />
        </DropdownMenuGroup>
      </DataTableRowActions>
      <Dialog open={pinOpen} onOpenChange={setPinOpen}>
        <DialogContent
          onCloseAutoFocus={(event) => {
            if (triggerRef.current) {
              event.preventDefault();
              triggerRef.current.focus();
            }
          }}
        >
          <DialogHeader>
            <DialogTitle>Pin {job.partName} to printer</DialogTitle>
          </DialogHeader>
          <Select value={printerId} onValueChange={setPrinterId}>
            <SelectTrigger className="w-full">
              <SelectValue placeholder="Choose a printer" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                {printers
                  .filter((p) => p.id)
                  .map((p) => (
                    <SelectItem key={p.id} value={p.id ?? ''}>
                      {p.name}
                    </SelectItem>
                  ))}
              </SelectGroup>
            </SelectContent>
          </Select>
          <DialogFooter>
            <DialogClose asChild>
              <Button variant="outline">Cancel</Button>
            </DialogClose>
            <Button
              disabled={!printerId || pin.isPending}
              onClick={() => pin.mutate({ id: jobId, data: { printerId } })}
            >
              {pin.isPending && <Spinner data-icon="inline-start" />}
              {pin.isPending ? 'Pinning…' : 'Pin to printer'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <RemoveJobButton
        jobId={jobId}
        open={removeOpen}
        onOpenChange={setRemoveOpen}
        hideTrigger
        returnFocusRef={triggerRef}
      />
    </>
  );
}
