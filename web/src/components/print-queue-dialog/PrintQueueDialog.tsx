import { MinusIcon, PlusIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { CheckerboardThumbnail } from '@/components/checkerboard-thumbnail/CheckerboardThumbnail';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { jobsStore, type MockJob } from '@/features/jobs/data/mockJobsStore';
import type { ListFleetResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsPrintersListFleetEndpoint } from '@/lib/api/generated/printers/printers';
import { formatMinutes } from '@/lib/formatMinutes';

/** Deliberately generic (not importing Parts' `DetectedColorSlot`) so this
 * shared dialog doesn't take a dependency on one feature's internals — the
 * caller resolves whichever slot/material summary is relevant into this
 * simple shape. Only Parts uses this dialog in this pass; a sibling
 * Assemblies feature has its own simpler queue behavior. */
export interface PrintQueuePart {
  name: string;
  estimatedTimeMinutes: number;
  weightGrams: number;
  primaryMaterial?: {
    colorHex: string;
    label: string;
  };
}

interface PrintQueueDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  part: PrintQueuePart;
}

type Priority = MockJob['priority'];

export function PrintQueueDialog({
  open,
  onOpenChange,
  part,
}: PrintQueueDialogProps) {
  const navigate = useNavigate();
  const fleetQuery = useOpenFarmApiEndpointsPrintersListFleetEndpoint();
  const printers =
    (fleetQuery.data?.data as ListFleetResponse | undefined)?.printers ?? [];

  const [printerId, setPrinterId] = useState('');
  const [quantity, setQuantity] = useState(1);
  const [priority, setPriority] = useState<Priority>('Normal');

  useEffect(() => {
    if (open) {
      setPrinterId('');
      setQuantity(1);
      setPriority('Normal');
    }
  }, [open]);

  const selectedPrinter = printers.find((p) => p.id === printerId);
  const estimatedQueueMinutes = part.estimatedTimeMinutes * quantity;

  function handleSubmit() {
    if (!selectedPrinter) return;

    const newJob: MockJob = {
      id: `job-${crypto.randomUUID()}`,
      name: `${part.name} ×${quantity}`,
      partName: part.name,
      priority,
      printerName: selectedPrinter.name ?? null,
      filamentColorHex: part.primaryMaterial?.colorHex,
      filamentMaterial: part.primaryMaterial?.label,
      status: 'queued',
      progressPercent: null,
      etaText: null,
    };

    jobsStore.setState((prev) => [...prev, newJob]);
    toast.success(`${newJob.name} added to the print queue.`);
    onOpenChange(false);
    navigate('/jobs');
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Send to queue</DialogTitle>
          <DialogDescription>
            Queue this part for printing on your fleet.
          </DialogDescription>
        </DialogHeader>

        <div className="flex items-center gap-3 rounded-lg border p-3">
          <CheckerboardThumbnail className="size-10 shrink-0 rounded-md" />
          <div className="flex flex-1 flex-col gap-0.5">
            <span className="font-medium">{part.name}</span>
            <span className="text-xs text-muted-foreground">
              {formatMinutes(part.estimatedTimeMinutes)} · {part.weightGrams} g
            </span>
          </div>
          {part.primaryMaterial ? (
            <span className="inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs">
              <Swatch colorHex={part.primaryMaterial.colorHex} size="sm" />
              {part.primaryMaterial.label}
            </span>
          ) : null}
        </div>

        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <span className="text-sm font-medium">Printer</span>
            <Select value={printerId} onValueChange={setPrinterId}>
              <SelectTrigger className="w-full">
                <SelectValue placeholder="Choose a printer" />
              </SelectTrigger>
              <SelectContent>
                {printers.map((printer) => (
                  <SelectItem key={printer.id} value={printer.id ?? ''}>
                    {printer.name} ({(printer.status ?? 'idle').toLowerCase()})
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-sm font-medium">Quantity</span>
            <div className="flex items-center gap-2">
              <Button
                type="button"
                variant="outline"
                size="icon-sm"
                aria-label="Decrease quantity"
                disabled={quantity <= 1}
                onClick={() => setQuantity((q) => Math.max(1, q - 1))}
              >
                <MinusIcon className="size-4" />
              </Button>
              <span className="w-8 text-center font-mono text-sm">
                {quantity}
              </span>
              <Button
                type="button"
                variant="outline"
                size="icon-sm"
                aria-label="Increase quantity"
                onClick={() => setQuantity((q) => q + 1)}
              >
                <PlusIcon className="size-4" />
              </Button>
            </div>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-sm font-medium">Priority</span>
            <ToggleGroup
              type="single"
              variant="outline"
              value={priority}
              onValueChange={(next) => {
                if (next) setPriority(next as Priority);
              }}
            >
              <ToggleGroupItem value="Low">Low</ToggleGroupItem>
              <ToggleGroupItem value="Normal">Normal</ToggleGroupItem>
              <ToggleGroupItem value="High">High</ToggleGroupItem>
            </ToggleGroup>
          </div>
        </div>

        <DialogFooter className="items-center sm:justify-between">
          <span className="text-sm text-muted-foreground">
            Estimated queue time · {formatMinutes(estimatedQueueMinutes)}
          </span>
          <div className="flex items-center gap-2">
            <Button
              type="button"
              variant="ghost"
              onClick={() => onOpenChange(false)}
            >
              Cancel
            </Button>
            <Button
              type="button"
              disabled={!selectedPrinter}
              onClick={handleSubmit}
            >
              Add to queue
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
