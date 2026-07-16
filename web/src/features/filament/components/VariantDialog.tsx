import { type FormEvent, useEffect, useState } from 'react';
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
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Switch } from '@/components/ui/switch';
import { profilesStore } from '@/features/profiles/data/mockProfilesStore';
import { cn } from '@/lib/utils';
import type { FilamentVariant } from '../data/mockFilamentStore';
import { NAMED_COLOR_SWATCHES } from '../lib/colorSwatches';
import { formatWeightGrams } from '../lib/weightLabel';

const WEIGHT_OPTIONS = [250, 500, 750, 1000] as const;
const NOZZLE_SIZES = ['0.4', '0.6'] as const;

function newVariantId() {
  return `variant-${crypto.randomUUID()}`;
}

function emptyVariant(): FilamentVariant {
  return {
    id: newVariantId(),
    name: '',
    colorHex: NAMED_COLOR_SWATCHES[0].hex,
    weightsGrams: [],
  };
}

interface VariantDialogProps {
  mode: 'add' | 'edit';
  open: boolean;
  onOpenChange: (open: boolean) => void;
  initialVariant?: FilamentVariant;
  onSave: (variant: FilamentVariant) => void;
}

export function VariantDialog({
  mode,
  open,
  onOpenChange,
  initialVariant,
  onSave,
}: VariantDialogProps) {
  const [variant, setVariant] = useState<FilamentVariant>(
    () => initialVariant ?? emptyVariant(),
  );

  useEffect(() => {
    if (open) {
      setVariant(initialVariant ?? emptyVariant());
    }
  }, [open, initialVariant]);

  const allProfiles = profilesStore.getSnapshot();

  function toggleWeight(grams: number) {
    setVariant((prev) => {
      const has = prev.weightsGrams.includes(grams);
      const weightsGrams = has
        ? prev.weightsGrams.filter((g) => g !== grams)
        : [...prev.weightsGrams, grams].sort((a, b) => a - b);
      return { ...prev, weightsGrams };
    });
  }

  function setOverrideEnabled(nozzleSize: '0.4' | '0.6', enabled: boolean) {
    setVariant((prev) => {
      const overrides = { ...prev.profileOverrides };
      if (enabled) {
        const firstMatch = allProfiles.find(
          (p) =>
            p.category === 'Filament' && String(p.nozzleSizeMm) === nozzleSize,
        );
        overrides[nozzleSize] = firstMatch?.id;
      } else {
        delete overrides[nozzleSize];
      }
      return { ...prev, profileOverrides: overrides };
    });
  }

  function setOverrideProfile(nozzleSize: '0.4' | '0.6', profileId: string) {
    setVariant((prev) => ({
      ...prev,
      profileOverrides: { ...prev.profileOverrides, [nozzleSize]: profileId },
    }));
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    onSave(variant);
  }

  const canSave =
    variant.name.trim().length > 0 && variant.weightsGrams.length > 0;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>
            {mode === 'add' ? 'Add variant' : 'Edit variant'}
          </DialogTitle>
          <DialogDescription>
            A colour variant of this filament, with the weights you keep it
            stocked in.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="variant-name">Name</Label>
            <Input
              id="variant-name"
              value={variant.name}
              onChange={(event) =>
                setVariant((prev) => ({ ...prev, name: event.target.value }))
              }
              placeholder="e.g. Black"
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label>Colour</Label>
            <div className="flex flex-wrap gap-2">
              {NAMED_COLOR_SWATCHES.map((option) => (
                <button
                  key={option.hex}
                  type="button"
                  aria-label={option.name}
                  aria-pressed={variant.colorHex === option.hex}
                  onClick={() =>
                    setVariant((prev) => ({ ...prev, colorHex: option.hex }))
                  }
                  className={cn(
                    'rounded-[6px] p-0.5 ring-1 ring-transparent transition-all',
                    variant.colorHex === option.hex &&
                      'ring-2 ring-ring ring-offset-1 ring-offset-background',
                  )}
                >
                  <Swatch colorHex={option.hex} size="lg" />
                </button>
              ))}
            </div>
          </div>

          <div className="flex flex-col gap-1.5">
            <Label>Weights</Label>
            <div className="flex flex-wrap gap-2">
              {WEIGHT_OPTIONS.map((grams) => {
                const selected = variant.weightsGrams.includes(grams);
                return (
                  <Button
                    key={grams}
                    type="button"
                    variant={selected ? 'default' : 'outline'}
                    size="sm"
                    onClick={() => toggleWeight(grams)}
                  >
                    {formatWeightGrams(grams)}
                  </Button>
                );
              })}
            </div>
          </div>

          <details className="rounded-lg border">
            <summary className="cursor-pointer px-3 py-2 text-sm font-medium">
              Advanced · profile overrides
            </summary>
            <div className="flex flex-col gap-4 border-t p-3">
              {NOZZLE_SIZES.map((nozzleSize) => {
                const matchingProfiles = allProfiles.filter(
                  (p) =>
                    p.category === 'Filament' &&
                    String(p.nozzleSizeMm) === nozzleSize,
                );
                const overrideId = variant.profileOverrides?.[nozzleSize];
                const enabled = overrideId !== undefined;
                const defaultProfile = matchingProfiles[0];
                return (
                  <div key={nozzleSize} className="flex flex-col gap-2">
                    <div className="flex items-center justify-between">
                      <Label
                        htmlFor={`override-${nozzleSize}`}
                        className="text-sm"
                      >
                        {nozzleSize}mm nozzle override
                      </Label>
                      <Switch
                        id={`override-${nozzleSize}`}
                        checked={enabled}
                        onCheckedChange={(checked) =>
                          setOverrideEnabled(nozzleSize, checked)
                        }
                      />
                    </div>
                    {enabled ? (
                      <Select
                        value={overrideId}
                        onValueChange={(value) =>
                          setOverrideProfile(nozzleSize, value)
                        }
                      >
                        <SelectTrigger className="w-full">
                          <SelectValue placeholder="Choose a profile" />
                        </SelectTrigger>
                        <SelectContent>
                          {matchingProfiles.map((profile) => (
                            <SelectItem key={profile.id} value={profile.id}>
                              {profile.name}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    ) : (
                      <p className="text-xs text-muted-foreground">
                        {defaultProfile
                          ? `Default: ${defaultProfile.name}`
                          : 'No default profile available for this nozzle size.'}
                      </p>
                    )}
                  </div>
                );
              })}
            </div>
          </details>

          <DialogFooter>
            <Button
              type="button"
              variant="ghost"
              onClick={() => onOpenChange(false)}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={!canSave}>
              {mode === 'add' ? 'Add variant' : 'Save variant'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
