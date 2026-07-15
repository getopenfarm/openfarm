import { CheckIcon, ChevronRightIcon, SparklesIcon } from 'lucide-react';
import { useState } from 'react';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { filamentStore } from '@/features/filament/data/mockFilamentStore';
import { cn } from '@/lib/utils';
import type { DetectedColorSlot } from '../data/mockPartsStore';
import { autoMatchColor } from '../lib/colorMatch';

interface MaterialAutoMatchSectionProps {
  colorSlots: DetectedColorSlot[];
  onChange: (next: DetectedColorSlot[]) => void;
}

export function MaterialAutoMatchSection({
  colorSlots,
  onChange,
}: MaterialAutoMatchSectionProps) {
  const [expandedIndex, setExpandedIndex] = useState<number | null>(null);
  const filaments = filamentStore.getSnapshot();

  function updateSlot(
    index: number,
    updater: (slot: DetectedColorSlot) => DetectedColorSlot,
  ) {
    onChange(colorSlots.map((slot, i) => (i === index ? updater(slot) : slot)));
  }

  function setAutoMatch(index: number) {
    updateSlot(index, (slot) => ({
      ...slot,
      pinnedFilamentId: undefined,
      pinnedVariantId: undefined,
    }));
    setExpandedIndex(null);
  }

  function setPinned(index: number, filamentId: string, variantId: string) {
    updateSlot(index, (slot) => ({
      ...slot,
      pinnedFilamentId: filamentId,
      pinnedVariantId: variantId,
    }));
    setExpandedIndex(null);
  }

  return (
    <div className="flex flex-col gap-2">
      <Label>Material matching</Label>
      <div className="flex flex-col divide-y rounded-xl border">
        {colorSlots.map((slot, index) => {
          const isPinned = Boolean(
            slot.pinnedFilamentId && slot.pinnedVariantId,
          );
          const pinnedFilament = isPinned
            ? filaments.find((f) => f.id === slot.pinnedFilamentId)
            : undefined;
          const pinnedVariant = pinnedFilament?.variants.find(
            (v) => v.id === slot.pinnedVariantId,
          );

          const autoMatch = !isPinned
            ? autoMatchColor(slot.detectedColorHex, slot.materialType)
            : null;
          const autoFilament = autoMatch
            ? filaments.find((f) => f.id === autoMatch.filamentId)
            : undefined;
          const autoVariant = autoFilament?.variants.find(
            (v) => v.id === autoMatch?.variantId,
          );

          const candidates = filaments.filter(
            (f) => f.materialType === slot.materialType,
          );
          const isExpanded = expandedIndex === index;

          return (
            <div key={slot.slotIndex} className="flex flex-col gap-3 p-3">
              <div className="flex flex-wrap items-center gap-3">
                <Swatch colorHex={slot.detectedColorHex} size="md" />
                <div className="flex flex-col">
                  <span className="text-sm font-medium">
                    {slot.materialType} · Slot {index + 1}
                  </span>
                  <span className="text-xs text-muted-foreground">
                    Detected color
                  </span>
                </div>

                <ChevronRightIcon className="size-4 shrink-0 text-muted-foreground" />

                {isPinned && pinnedVariant && pinnedFilament ? (
                  <div className="flex items-center gap-2">
                    <Swatch colorHex={pinnedVariant.colorHex} size="md" />
                    <div className="flex flex-col">
                      <span className="text-sm font-medium">
                        {pinnedFilament.brand} {pinnedFilament.material}
                      </span>
                      <span className="text-xs text-muted-foreground">
                        {pinnedVariant.name}
                      </span>
                    </div>
                    <StatusBadge kind="violet" label="Specified" />
                  </div>
                ) : autoVariant && autoFilament ? (
                  <div className="flex items-center gap-2">
                    <Swatch colorHex={autoVariant.colorHex} size="md" />
                    <div className="flex flex-col">
                      <span className="text-sm font-medium">
                        {autoFilament.brand} {autoFilament.material}
                      </span>
                      <span className="text-xs text-muted-foreground">
                        {autoVariant.name}
                      </span>
                    </div>
                    <StatusBadge kind="green" label="Auto" />
                  </div>
                ) : (
                  <div className="flex items-center gap-2">
                    <Swatch auto size="md" />
                    <span className="text-sm text-muted-foreground">
                      No match yet
                    </span>
                  </div>
                )}

                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="ml-auto"
                  onClick={() =>
                    setExpandedIndex((current) =>
                      current === index ? null : index,
                    )
                  }
                >
                  {isExpanded ? 'Close' : isPinned ? 'Change' : 'Configure'}
                </Button>
              </div>

              {isExpanded ? (
                <div className="flex flex-col gap-2 rounded-lg border bg-muted/30 p-2">
                  <button
                    type="button"
                    onClick={() => setAutoMatch(index)}
                    className={cn(
                      'flex items-center gap-2 rounded-md border p-2 text-left text-sm transition-colors hover:bg-muted',
                      !isPinned && 'border-kind-violet bg-kind-violet/5',
                    )}
                  >
                    <SparklesIcon className="size-4 text-kind-violet" />
                    Match automatically
                    {!isPinned ? (
                      <CheckIcon className="ml-auto size-4 text-kind-violet" />
                    ) : null}
                  </button>

                  {candidates.length === 0 ? (
                    <p className="p-2 text-xs text-muted-foreground">
                      No {slot.materialType} filaments in your library yet.
                    </p>
                  ) : (
                    candidates.flatMap((filament) =>
                      filament.variants.map((variant) => {
                        const selected = slot.pinnedVariantId === variant.id;
                        return (
                          <button
                            key={variant.id}
                            type="button"
                            onClick={() =>
                              setPinned(index, filament.id, variant.id)
                            }
                            className={cn(
                              'flex items-center gap-2 rounded-md border p-2 text-left text-sm transition-colors hover:bg-muted',
                              selected && 'border-kind-violet bg-kind-violet/5',
                            )}
                          >
                            <Swatch colorHex={variant.colorHex} size="sm" />
                            <span>
                              {filament.brand} {filament.material}
                            </span>
                            <span className="text-muted-foreground">
                              {variant.name}
                            </span>
                            {selected ? (
                              <CheckIcon className="ml-auto size-4 text-kind-violet" />
                            ) : null}
                          </button>
                        );
                      }),
                    )
                  )}
                </div>
              ) : null}
            </div>
          );
        })}
      </div>
    </div>
  );
}
