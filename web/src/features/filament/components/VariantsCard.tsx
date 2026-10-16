import { PlusIcon, Trash2Icon } from 'lucide-react';
import { useState } from 'react';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { type NewFilamentVariant, VariantDialog } from './VariantDialog';

export interface DisplayedVariant {
  id?: string;
  colorName?: string;
  colorHex?: string;
}

interface VariantsCardProps {
  variants: DisplayedVariant[];
  onAdd: (variant: NewFilamentVariant) => void;
  /** Shown only for variants without an id (staged, not yet saved). */
  onRemove?: (index: number) => void;
  isAdding?: boolean;
}

export function VariantsCard({
  variants,
  onAdd,
  onRemove,
  isAdding,
}: VariantsCardProps) {
  const [dialogOpen, setDialogOpen] = useState(false);

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between">
        <CardTitle>Variants ({variants.length})</CardTitle>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={isAdding}
          onClick={() => setDialogOpen(true)}
        >
          <PlusIcon data-icon="inline-start" /> Add variant
        </Button>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        {variants.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            No variants
          </p>
        ) : (
          variants.map((variant, index) => (
            <div
              key={variant.id ?? `${variant.colorName}-${index}`}
              className="flex items-center gap-3 rounded-lg border px-3 py-2"
            >
              <Swatch colorHex={variant.colorHex} size="md" />
              <span className="flex-1 text-sm font-medium">
                {variant.colorName}
              </span>
              {onRemove && !variant.id ? (
                <Button
                  type="button"
                  variant="ghost"
                  size="icon-sm"
                  aria-label={`Remove ${variant.colorName}`}
                  onClick={() => onRemove(index)}
                >
                  <Trash2Icon
                    data-icon="inline-start"
                    className="text-destructive"
                  />
                </Button>
              ) : null}
            </div>
          ))
        )}
      </CardContent>

      <VariantDialog
        open={dialogOpen}
        onOpenChange={setDialogOpen}
        onSave={(variant) => {
          onAdd(variant);
          setDialogOpen(false);
        }}
      />
    </Card>
  );
}
