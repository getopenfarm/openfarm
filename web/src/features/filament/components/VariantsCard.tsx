import { PencilIcon, PlusIcon, Trash2Icon } from 'lucide-react';
import { useState } from 'react';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { Swatch } from '@/components/swatch/Swatch';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardAction,
  CardContent,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import type {
  FilamentProfileAssignmentInput,
  FilamentProfileAssignmentIssue,
} from '@/lib/api/generated/endpoints.schemas';
import { formatCost } from '@/lib/formatUnits';
import {
  type FilamentVariantDraft,
  VariantDialog,
  type VariantDialogResult,
} from './VariantDialog';

interface VariantsCardProps {
  variants: FilamentVariantDraft[];
  /** The filament's price per kg, used by variants without their own. */
  inheritedPrice: number | null;
  materialType: string;
  assignments: FilamentProfileAssignmentInput[];
  issues?: FilamentProfileAssignmentIssue[];
  /** `index` is null for a new variant. */
  onSave: (index: number | null, result: VariantDialogResult) => void;
  /** Shown only for variants without an id (staged, not yet saved). */
  onRemove?: (index: number) => void;
}

function VariantPrice({
  price,
  inherited,
}: {
  price: number | null;
  inherited: number | null;
}) {
  if (price != null) {
    return <span>{formatCost(price)}/kg</span>;
  }
  return (
    <span className="text-muted-foreground">
      {inherited == null ? 'No price' : `${formatCost(inherited)}/kg default`}
    </span>
  );
}

export function VariantsCard({
  variants,
  inheritedPrice,
  materialType,
  assignments,
  issues = [],
  onSave,
  onRemove,
}: VariantsCardProps) {
  const [dialogOpen, setDialogOpen] = useState(false);
  // null while adding; otherwise the index of the variant being edited.
  const [editIndex, setEditIndex] = useState<number | null>(null);

  function openDialog(index: number | null) {
    setEditIndex(index);
    setDialogOpen(true);
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Variants ({variants.length})</CardTitle>
        <CardAction>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => openDialog(null)}
          >
            <PlusIcon data-icon="inline-start" /> Add variant
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent>
        {variants.length === 0 ? (
          <EmptyState
            size="sm"
            title="No variants"
            description="Add one variant per color."
          />
        ) : (
          <ul className="divide-y rounded-lg border">
            {variants.map((variant, index) => {
              const overrideCount = assignments.filter(
                (a) => a.colorHex === variant.colorHex,
              ).length;
              const hasIssue = issues.some(
                (issue) => issue.assignment?.colorHex === variant.colorHex,
              );
              return (
                <li
                  key={variant.id ?? `${variant.colorName}-${index}`}
                  className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2 text-sm"
                >
                  <Swatch colorHex={variant.colorHex} size="md" />
                  <span className="flex-1 font-medium">
                    {variant.colorName}
                  </span>
                  {hasIssue && <Badge kind="red">Invalid assignment</Badge>}
                  {overrideCount > 0 && (
                    <span className="text-muted-foreground">
                      {overrideCount} profile override
                      {overrideCount === 1 ? '' : 's'}
                    </span>
                  )}
                  <span className="min-w-28 text-right tabular-nums">
                    <VariantPrice
                      price={variant.pricePerKg}
                      inherited={inheritedPrice}
                    />
                  </span>
                  <div className="flex">
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`Edit ${variant.colorName}`}
                      onClick={() => openDialog(index)}
                    >
                      <PencilIcon />
                    </Button>
                    {onRemove && !variant.id ? (
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon-sm"
                        aria-label={`Remove ${variant.colorName}`}
                        onClick={() => onRemove(index)}
                      >
                        <Trash2Icon className="text-destructive" />
                      </Button>
                    ) : null}
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </CardContent>

      <VariantDialog
        open={dialogOpen}
        onOpenChange={setDialogOpen}
        variant={editIndex == null ? undefined : variants[editIndex]}
        inheritedPrice={inheritedPrice}
        materialType={materialType}
        assignments={assignments}
        onSave={(result) => {
          onSave(editIndex, result);
          setDialogOpen(false);
        }}
      />
    </Card>
  );
}
