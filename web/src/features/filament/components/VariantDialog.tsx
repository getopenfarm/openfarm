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
import {
  Field,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { cn } from '@/lib/utils';
import { NAMED_COLOR_SWATCHES } from '../lib/colorSwatches';

export interface NewFilamentVariant {
  colorName: string;
  colorHex: string;
}

function emptyVariant(): NewFilamentVariant {
  const [first] = NAMED_COLOR_SWATCHES;
  return { colorName: first.name, colorHex: first.hex };
}

interface VariantDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSave: (variant: NewFilamentVariant) => void;
}

export function VariantDialog({
  open,
  onOpenChange,
  onSave,
}: VariantDialogProps) {
  const [variant, setVariant] = useState<NewFilamentVariant>(emptyVariant());

  useEffect(() => {
    if (open) {
      setVariant(emptyVariant());
    }
  }, [open]);

  function handleSubmit(event: FormEvent) {
    // Also stops the submit from bubbling to an ancestor <form> in the React tree — this dialog
    // is a self-contained quick action that may be rendered inside a page's own form (e.g.
    // FilamentFormPage), and React's portal event bubbling follows the component tree, not the
    // DOM tree, so an un-stopped submit here would also trigger the outer form's onSubmit.
    event.preventDefault();
    event.stopPropagation();
    onSave(variant);
  }

  const canSave = variant.colorName.trim().length > 0;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Add variant</DialogTitle>
          <DialogDescription>Color of this filament.</DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="flex flex-col gap-4">
          <FieldGroup className="gap-4">
            <Field className="flex flex-col gap-1.5">
              <FieldLabel htmlFor="variant-color-name">Name</FieldLabel>
              <Input
                id="variant-color-name"
                value={variant.colorName}
                onChange={(event) =>
                  setVariant((prev) => ({
                    ...prev,
                    colorName: event.target.value,
                  }))
                }
                placeholder="e.g. Black"
              />
            </Field>

            <FieldSet>
              <FieldLegend variant="label">Color</FieldLegend>
              <div className="flex flex-wrap gap-2">
                {NAMED_COLOR_SWATCHES.map((option) => (
                  <button
                    key={option.hex}
                    type="button"
                    aria-label={option.name}
                    aria-pressed={variant.colorHex === option.hex}
                    onClick={() =>
                      setVariant((prev) => ({
                        ...prev,
                        colorHex: option.hex,
                        // Name follows the swatch until the user types a custom one.
                        colorName:
                          !prev.colorName ||
                          NAMED_COLOR_SWATCHES.some(
                            (swatch) => swatch.name === prev.colorName,
                          )
                            ? option.name
                            : prev.colorName,
                      }))
                    }
                    className={cn(
                      'flex rounded-[6px] p-0.5 ring-1 ring-transparent transition-all',
                      variant.colorHex === option.hex &&
                        'ring-2 ring-ring ring-offset-1 ring-offset-background',
                    )}
                  >
                    <Swatch colorHex={option.hex} size="lg" />
                  </button>
                ))}
              </div>
            </FieldSet>

            <DialogFooter>
              <Button
                type="button"
                variant="outline"
                onClick={() => onOpenChange(false)}
              >
                Cancel
              </Button>
              <Button type="submit" disabled={!canSave}>
                Add variant
              </Button>
            </DialogFooter>
          </FieldGroup>
        </form>
      </DialogContent>
    </Dialog>
  );
}
