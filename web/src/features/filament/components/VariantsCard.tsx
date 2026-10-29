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
import { useCurrency } from '@/features/settings/generalSettings';
import type {
  FilamentProfileAssignmentInput,
  FilamentProfileAssignmentIssue,
  VariantStockResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { formatCost, formatWeight } from '@/lib/formatUnits';
import { pluralize } from '@/lib/pluralize';
import {
  type FilamentVariantDraft,
  VariantDialog,
  type VariantDialogResult,
} from './VariantDialog';

interface VariantsCardProps {
  variants: FilamentVariantDraft[];
  /** The filament's price per kg, used by variants without their own. */
  inheritedPrice: number | null;
  /** The filament's low-stock threshold, used by stocked variants without their own. */
  inheritedLowStock: number | null;
  /** The filament's days-left threshold, used by variants without their own. */
  inheritedLowStockDays: number | null;
  /** Saved variants' stock; staged ones have none yet. */
  stock?: VariantStockResponse[];
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
  const currency = useCurrency();
  if (price != null) {
    return <span>{formatCost(price, currency)}/kg</span>;
  }
  return (
    <span className="text-muted-foreground">
      {inherited == null
        ? 'No price'
        : `${formatCost(inherited, currency)}/kg default`}
    </span>
  );
}

function VariantStock({ stock }: { stock: VariantStockResponse }) {
  const used = stock.usedLast30DaysGrams ?? 0;
  return (
    <span className="w-full pl-8 text-xs text-muted-foreground tabular-nums">
      {stock.activeSpoolCount
        ? `${formatWeight(stock.onHandGrams ?? 0)} in ${pluralize(stock.activeSpoolCount, 'spool')}`
        : 'No spools'}
      {used > 0 && ` · ${formatWeight(used)} used in 30 d`}
    </span>
  );
}

export function VariantsCard({
  variants,
  inheritedPrice,
  inheritedLowStock,
  inheritedLowStockDays,
  stock = [],
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
              const variantStock = stock.find(
                (row) => row.filamentVariantId === variant.id,
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
                  {variantStock?.isLow && <Badge kind="amber">Low</Badge>}
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
                  {variantStock && <VariantStock stock={variantStock} />}
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
        inheritedLowStock={inheritedLowStock}
        inheritedLowStockDays={inheritedLowStockDays}
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
