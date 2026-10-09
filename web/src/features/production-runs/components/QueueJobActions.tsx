import { useQueryClient } from '@tanstack/react-query';
import {
  ArrowUpToLineIcon,
  MoreHorizontalIcon,
  PinIcon,
  PinOffIcon,
  Trash2Icon,
} from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type {
  FleetPrinterResponse,
  QueueJobRowResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintJobsListQueueJobsEndpointQueryKey,
  useOpenFarmApiEndpointsPrintJobsPinPrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsUnpinPrintJobEndpoint,
} from '@/lib/api/generated/print-jobs/print-jobs';
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
        toast.error(await extractErrorDetail(error, 'Could not pin this job.')),
    },
  });
  const unpin = useOpenFarmApiEndpointsPrintJobsUnpinPrintJobEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Job unpinned.');
        invalidate();
      },
      onError: async (error) =>
        toast.error(
          await extractErrorDetail(error, 'Could not unpin this job.'),
        ),
    },
  });
  const jobId = job.jobId;
  if (!jobId) return null;
  const waiting = job.status === 'Queued' && !job.dispatchedPrinterId;
  if (!waiting && job.status !== 'Printing' && job.status !== 'Paused')
    return null;
  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={`Actions for ${job.partName}`}
          >
            <MoreHorizontalIcon />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
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
              <DropdownMenuItem
                variant="destructive"
                onSelect={() => setRemoveOpen(true)}
              >
                <Trash2Icon />
                Remove job
              </DropdownMenuItem>
            </>
          )}
          <JobRemoteControls
            jobId={jobId}
            status={job.status ?? ''}
            menu
            pending={pending}
            onPendingChange={onPendingChange}
          />
        </DropdownMenuContent>
      </DropdownMenu>
      <Dialog open={pinOpen} onOpenChange={setPinOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Pin {job.partName} to printer</DialogTitle>
          </DialogHeader>
          <Select value={printerId} onValueChange={setPrinterId}>
            <SelectTrigger className="w-full">
              <SelectValue placeholder="Choose a printer" />
            </SelectTrigger>
            <SelectContent>
              {printers
                .filter((p) => p.id)
                .map((p) => (
                  <SelectItem key={p.id} value={p.id ?? ''}>
                    {p.name}
                  </SelectItem>
                ))}
            </SelectContent>
          </Select>
          <Button
            disabled={!printerId || pin.isPending}
            onClick={() => pin.mutate({ id: jobId, data: { printerId } })}
          >
            {pin.isPending ? 'Pinning…' : 'Pin to printer'}
          </Button>
        </DialogContent>
      </Dialog>
      <RemoveJobButton
        jobId={jobId}
        open={removeOpen}
        onOpenChange={setRemoveOpen}
        hideTrigger
      />
    </>
  );
}
