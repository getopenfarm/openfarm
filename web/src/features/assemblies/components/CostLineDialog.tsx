import { type FormEvent, useEffect, useState } from 'react';
import { PermissionButton as Button } from '@/components/PermissionButton';
import {
  Dialog,
  DialogDescription,
  DialogScrollableContent,
  DialogTitle,
} from '@/components/ui/dialog';
import { FieldDescription, FieldGroup } from '@/components/ui/field';
import {
  Choice,
  DimensionFields,
  NumberField,
} from '@/features/cost-catalog/CostFields';
import {
  categoryLabel,
  formatQuantity,
  formatRate,
  unitLabels,
  usage,
} from '@/features/cost-catalog/costing';
import { useCurrency } from '@/features/settings/generalSettings';
import type {
  AssemblyCostInput,
  CostItemResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { formatCost } from '@/lib/formatUnits';

interface Draft {
  purpose: string;
  inputMode: 'Quantity' | 'Dimensions';
  quantity: number | null;
  length: number | null;
  lengthUnit: string;
  width: number | null;
  widthUnit: string;
  pieces: number | null;
}

// A usage of 0 means "not entered yet", so it seeds as blank.
function toDraft(line: AssemblyCostInput): Draft {
  return {
    purpose: line.purpose ?? 'Production',
    inputMode: line.inputMode === 'Dimensions' ? 'Dimensions' : 'Quantity',
    quantity: line.quantity ? line.quantity : null,
    length: line.length ?? null,
    lengthUnit: line.lengthUnit ?? 'cm',
    width: line.width ?? null,
    widthUnit: line.widthUnit ?? 'cm',
    pieces: line.pieces ?? 1,
  };
}

// Cut fields are sent only in Dimensions mode, so a saved line never carries stale ones.
function toLine(
  line: AssemblyCostInput,
  draft: Draft,
  sized: boolean,
): AssemblyCostInput {
  return {
    catalogItemId: line.catalogItemId,
    purpose: draft.purpose,
    inputMode: sized ? 'Dimensions' : 'Quantity',
    quantity: draft.quantity ?? (sized ? 1 : 0),
    pieces: draft.pieces ?? 1,
    ...(sized
      ? {
          length: draft.length ?? 0,
          lengthUnit: draft.lengthUnit,
          width: draft.width ?? 0,
          widthUnit: draft.widthUnit,
        }
      : {}),
  };
}

const positive = (value: number | null): value is number =>
  value != null && Number.isFinite(value) && value > 0;

interface CostLineDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  line: AssemblyCostInput | null;
  item: CostItemResponse | undefined;
  onSave: (line: AssemblyCostInput) => void;
}

export function CostLineDialog({
  open,
  onOpenChange,
  line,
  item,
  onSave,
}: CostLineDialogProps) {
  const currency = useCurrency();
  const [draft, setDraft] = useState<Draft>(() =>
    line ? toDraft(line) : toDraft({ catalogItemId: '' }),
  );

  // Seed only when the dialog opens, so a background refetch can't reset in-progress edits.
  // biome-ignore lint/correctness/useExhaustiveDependencies: seed on open only
  useEffect(() => {
    if (open && line) setDraft(toDraft(line));
  }, [open]);

  const unit = item?.pricing?.unit ?? 'Piece';
  const sized = unit === 'SquareMetre' && draft.inputMode === 'Dimensions';
  const valid = sized
    ? positive(draft.length) &&
      positive(draft.width) &&
      positive(draft.pieces) &&
      Number.isInteger(draft.pieces)
    : positive(draft.quantity) &&
      (unit !== 'Piece' || Number.isInteger(draft.quantity));
  const next = line ? toLine(line, draft, sized) : null;
  const consumed = next && valid ? usage(next, unit) : 0;
  const rate = item?.effectiveRate;
  const cost = rate == null ? null : consumed * rate;
  const name = item?.name ?? 'Item unavailable';
  const category = categoryLabel(item?.pricing?.category ?? 'Other');
  const rateText =
    rate == null
      ? 'Not priced'
      : `${formatRate(rate, currency)} / ${unitLabels[unit]}`;
  const usageLabel =
    unit === 'Hour'
      ? 'Minutes'
      : unit === 'SquareMetre'
        ? 'Area (m²)'
        : `Quantity (${unitLabels[unit]})`;

  function handleSubmit(event: FormEvent) {
    // This dialog renders inside the assembly form's React tree, so an un-stopped submit would
    // also save the whole assembly (same reason as VariantDialog).
    event.preventDefault();
    event.stopPropagation();
    if (valid && next) onSave(next);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogScrollableContent
        className="sm:max-w-lg"
        onOpenAutoFocus={(event) => {
          event.preventDefault();
          // Select the value so typing replaces it.
          const input = (
            event.currentTarget as HTMLElement
          ).querySelector<HTMLInputElement>('form input');
          input?.focus();
          input?.select();
        }}
        header={
          <>
            <DialogTitle>Edit {name}</DialogTitle>
            <DialogDescription>
              {category} · {rateText}
            </DialogDescription>
          </>
        }
        footer={
          <>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
            >
              Cancel
            </Button>
            <Button
              permission="assemblies:create|assemblies:edit"
              type="submit"
              form="cost-line-form"
              disabled={!valid}
            >
              Update cost item
            </Button>
          </>
        }
      >
        <form id="cost-line-form" noValidate onSubmit={handleSubmit}>
          <FieldGroup className="gap-4">
            <div className="flex flex-col gap-2">
              <Choice
                label="Counts as"
                value={draft.purpose}
                options={[
                  { value: 'Production', label: 'Production' },
                  {
                    value: 'FinalProduct',
                    label: 'Final packing and delivery',
                  },
                ]}
                onChange={(purpose) => setDraft({ ...draft, purpose })}
              />
              <FieldDescription>
                {draft.purpose === 'FinalProduct'
                  ? 'Counted only when this is the finished product.'
                  : 'Also counted when this assembly is used in a parent assembly.'}
              </FieldDescription>
            </div>
            {unit === 'SquareMetre' && (
              <Choice
                label="Usage method"
                value={draft.inputMode}
                options={[
                  { value: 'Quantity', label: 'Area in m²' },
                  { value: 'Dimensions', label: 'Length × width' },
                ]}
                onChange={(inputMode) =>
                  setDraft({
                    ...draft,
                    inputMode: inputMode as Draft['inputMode'],
                  })
                }
              />
            )}
            {sized ? (
              <DimensionFields
                {...draft}
                onChange={(change) => setDraft({ ...draft, ...change })}
              >
                <NumberField
                  label="Identical cuts"
                  value={draft.pieces}
                  whole
                  required
                  onChange={(pieces) => setDraft({ ...draft, pieces })}
                />
              </DimensionFields>
            ) : (
              <NumberField
                label={usageLabel}
                value={draft.quantity}
                whole={unit === 'Piece'}
                required
                onChange={(quantity) => setDraft({ ...draft, quantity })}
              />
            )}
            {valid && (
              <FieldDescription>
                {unit === 'Hour'
                  ? `${formatQuantity(draft.quantity ?? 0)} min × ${formatRate(rate, currency)} / hour`
                  : `${formatQuantity(consumed)} ${unitLabels[unit]} × ${formatRate(rate, currency)}`}{' '}
                = {cost == null ? 'Not priced' : formatCost(cost, currency)} per
                product
              </FieldDescription>
            )}
            {sized && (
              <FieldDescription>
                Area estimate assumes reusable offcuts; no cutting waste is
                added.
              </FieldDescription>
            )}
          </FieldGroup>
        </form>
      </DialogScrollableContent>
    </Dialog>
  );
}
