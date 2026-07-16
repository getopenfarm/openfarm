import { MinusIcon, PlusIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { partsStore } from '@/features/parts/data/mockPartsStore';
import { formatMinutes } from '@/lib/formatMinutes';

export interface PartPickerResult {
  partId: string;
  quantity: number;
}

interface PartPickerDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  mode: 'add' | 'edit';
  initialPartId?: string;
  initialQuantity?: number;
  onSubmit: (result: PartPickerResult) => void;
}

/** Reused both to add a new part component and to edit an existing one's
 * part/quantity — `mode` drives the title/submit label only, the fields are
 * identical either way. */
export function PartPickerDialog({
  open,
  onOpenChange,
  mode,
  initialPartId,
  initialQuantity,
  onSubmit,
}: PartPickerDialogProps) {
  const parts = partsStore.getSnapshot();

  const [partId, setPartId] = useState(initialPartId ?? parts[0]?.id ?? '');
  const [quantity, setQuantity] = useState(initialQuantity ?? 1);

  // Reset local state whenever the dialog opens so a previous edit/add
  // never leaks into the next interaction.
  useEffect(() => {
    if (open) {
      setPartId(initialPartId ?? parts[0]?.id ?? '');
      setQuantity(initialQuantity ?? 1);
    }
  }, [open, initialPartId, initialQuantity, parts[0]?.id]);

  const selectedPart = parts.find((part) => part.id === partId);

  function handleSubmit() {
    if (!partId) return;
    onSubmit({ partId, quantity: Math.max(1, quantity) });
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle>{mode === 'add' ? 'Add part' : 'Edit part'}</DialogTitle>
          <DialogDescription>
            Pick a part from your library and set how many are used in this
            assembly.
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label>Part</Label>
            <Select value={partId} onValueChange={setPartId}>
              <SelectTrigger className="w-full">
                <SelectValue placeholder="Select a part" />
              </SelectTrigger>
              <SelectContent>
                {parts.map((part) => (
                  <SelectItem key={part.id} value={part.id}>
                    {part.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-xs text-muted-foreground">
              {selectedPart
                ? `${formatMinutes(selectedPart.estimatedTimeMinutes)} · ${selectedPart.weightGrams} g · v${selectedPart.revision}`
                : 'No sliced file'}
            </p>
          </div>

          <div className="flex items-center justify-between">
            <Label>Quantity</Label>
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
        </div>

        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
          >
            Cancel
          </Button>
          <Button type="button" disabled={!partId} onClick={handleSubmit}>
            {mode === 'add' ? 'Add part' : 'Save'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
