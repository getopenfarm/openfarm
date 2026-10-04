import { useQueryClient } from '@tanstack/react-query';
import { AlertTriangleIcon, PinIcon, PinOffIcon } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import type { QueueJobRowResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintJobsListQueueJobsEndpointQueryKey,
  useOpenFarmApiEndpointsPrintJobsPinPrintJobEndpoint,
  useOpenFarmApiEndpointsPrintJobsUnpinPrintJobEndpoint,
} from '@/lib/api/generated/print-jobs/print-jobs';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { JobRemoteControls } from './JobRemoteControls';

const STATUS_VARIANT: Record<
  string,
  'default' | 'secondary' | 'outline' | 'destructive'
> = {
  Queued: 'secondary',
  Printing: 'default',
  Paused: 'outline',
  Completed: 'secondary',
  Failed: 'destructive',
  Cancelled: 'destructive',
};

interface QueueJobRowProps {
  job: QueueJobRowResponse;
  /** Native HTML5 drag-and-drop hooks — wired by QueuePage's virtualized list (T085). */
  draggable?: boolean;
  onDragStart?: () => void;
  onDragOver?: (event: React.DragEvent) => void;
  onDrop?: () => void;
}

/** One queue row — status, isBlocked flag, pin/unpin, and remote controls (tasks.md T071/T086). */
export function QueueJobRow({
  job,
  draggable,
  onDragStart,
  onDragOver,
  onDrop,
}: QueueJobRowProps) {
  const queryClient = useQueryClient();
  const [pinPickerOpen, setPinPickerOpen] = useState(false);
  const [selectedPrinterId, setSelectedPrinterId] = useState('');

  const printersQuery = useOpenFarmApiEndpointsPrintersListFleetEndpoint();
  const printers = printersQuery.data?.data?.printers ?? [];
  const pinnedPrinterName = printers.find(
    (p) => p.id === job.pinnedPrinterId,
  )?.name;

  function invalidateQueue() {
    queryClient.invalidateQueries({
      queryKey: getOpenFarmApiEndpointsPrintJobsListQueueJobsEndpointQueryKey(),
    });
  }

  const pinMutation = useOpenFarmApiEndpointsPrintJobsPinPrintJobEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Job pinned.');
        invalidateQueue();
      },
      onError: async (error) => {
        toast.error(await extractErrorDetail(error, 'Could not pin this job.'));
      },
    },
  });

  const unpinMutation = useOpenFarmApiEndpointsPrintJobsUnpinPrintJobEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Job unpinned.');
        invalidateQueue();
      },
      onError: async (error) => {
        toast.error(
          await extractErrorDetail(error, 'Could not unpin this job.'),
        );
      },
    },
  });

  function handlePin() {
    if (!job.jobId || !selectedPrinterId) return;
    pinMutation.mutate({
      id: job.jobId,
      data: { printerId: selectedPrinterId },
    });
    setPinPickerOpen(false);
    setSelectedPrinterId('');
  }

  return (
    <li
      draggable={draggable}
      onDragStart={onDragStart}
      onDragOver={onDragOver}
      onDrop={onDrop}
      className="flex list-none items-center gap-3 border-b bg-background px-3 py-2 last:border-b-0"
    >
      <div className="flex min-w-0 flex-1 flex-col">
        <Link
          to={`/jobs/${job.jobId}`}
          className="truncate text-sm font-medium hover:underline"
        >
          {job.partName}
        </Link>
        <Link
          to={`/production-runs/${job.productionRunId}`}
          className="truncate text-xs text-muted-foreground hover:underline"
        >
          {job.runDisplayLabel}
        </Link>
      </div>

      <span className="w-16 shrink-0 text-xs text-muted-foreground">
        ×{job.plateQuantity}
      </span>

      {job.isBlocked && (
        <span
          className="flex shrink-0 items-center gap-1 text-xs text-destructive"
          title={
            job.assignmentMode === 'Pinned'
              ? `Blocked on ${pinnedPrinterName ?? 'its pinned printer'}`
              : 'No available printer matches the nozzle and loaded material requirements'
          }
        >
          <AlertTriangleIcon className="size-3.5" />{' '}
          {job.assignmentMode === 'Pinned'
            ? `Blocked on ${pinnedPrinterName ?? 'printer'}`
            : 'No matching printer'}
        </span>
      )}

      <Badge
        variant={
          job.status ? (STATUS_VARIANT[job.status] ?? 'outline') : 'outline'
        }
        className="shrink-0"
      >
        {job.status}
      </Badge>

      {job.status === 'Queued' && (
        <div className="shrink-0">
          {job.assignmentMode === 'Pinned' ? (
            <Button
              type="button"
              size="sm"
              variant="ghost"
              disabled={unpinMutation.isPending}
              onClick={() =>
                job.jobId && unpinMutation.mutate({ id: job.jobId })
              }
            >
              <PinOffIcon className="size-3.5" />
              Unpin
            </Button>
          ) : (
            <Popover open={pinPickerOpen} onOpenChange={setPinPickerOpen}>
              <PopoverTrigger asChild>
                <Button type="button" size="sm" variant="ghost">
                  <PinIcon className="size-3.5" />
                  Pin
                </Button>
              </PopoverTrigger>
              <PopoverContent className="flex w-56 flex-col gap-2">
                <Select
                  value={selectedPrinterId}
                  onValueChange={setSelectedPrinterId}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue placeholder="Choose a printer" />
                  </SelectTrigger>
                  <SelectContent>
                    {printers.map((printer) => (
                      <SelectItem key={printer.id} value={printer.id ?? ''}>
                        {printer.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <Button
                  type="button"
                  size="sm"
                  disabled={!selectedPrinterId || pinMutation.isPending}
                  onClick={handlePin}
                >
                  Pin to printer
                </Button>
              </PopoverContent>
            </Popover>
          )}
        </div>
      )}

      <div className="w-20 shrink-0">
        {job.jobId && job.status && (
          <JobRemoteControls jobId={job.jobId} status={job.status} />
        )}
      </div>
    </li>
  );
}
