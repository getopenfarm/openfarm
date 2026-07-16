import { PrinterIcon } from 'lucide-react';
import { Link } from 'react-router';
import { CheckerboardThumbnail } from '@/components/checkerboard-thumbnail/CheckerboardThumbnail';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import { formatMinutes } from '@/lib/formatMinutes';
import type { Part } from '../data/mockPartsStore';
import { resolveSlotDisplayColor } from '../lib/colorMatch';

interface PartCardProps {
  part: Part;
  onPrint: () => void;
}

export function PartCard({ part, onPrint }: PartCardProps) {
  const primarySlot = part.colorSlots[0];

  return (
    <Link
      to={`/parts/${part.id}/edit`}
      className="group flex flex-col gap-3 rounded-xl border p-3 transition-colors hover:bg-muted/40"
    >
      <div className="relative">
        <CheckerboardThumbnail
          label={part.fileFormat}
          className="h-[84px] w-full"
        />
        <Button
          type="button"
          variant="secondary"
          size="icon-sm"
          aria-label={`Print ${part.name}`}
          className="absolute top-1.5 right-1.5 opacity-0 shadow-sm transition-opacity group-hover:opacity-100 focus-visible:opacity-100"
          onClick={(event) => {
            event.preventDefault();
            event.stopPropagation();
            onPrint();
          }}
        >
          <PrinterIcon className="size-4" />
        </Button>
      </div>

      <div className="flex items-center justify-between gap-2">
        <span className="truncate font-medium">{part.name}</span>
        <span className="shrink-0 font-mono text-xs text-muted-foreground">
          v{part.revision}
        </span>
      </div>

      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-1.5">
          {part.colorSlots.slice(0, 3).map((slot) => (
            <Swatch
              key={slot.slotIndex}
              colorHex={resolveSlotDisplayColor(slot)}
              size="sm"
            />
          ))}
          {primarySlot ? (
            <span className="text-xs text-muted-foreground">
              {primarySlot.materialType}
            </span>
          ) : null}
        </div>
        <span className="font-mono text-xs text-muted-foreground">
          {formatMinutes(part.estimatedTimeMinutes)}
        </span>
      </div>
    </Link>
  );
}
