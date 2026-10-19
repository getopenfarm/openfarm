import { RotateCcwIcon } from 'lucide-react';
import { type FormEvent, useEffect, useState } from 'react';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogDescription,
  DialogScrollableContent,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { ProfilePickerCombobox } from '@/features/profiles/components/ProfilePickerCombobox';
import type { FilamentProfileAssignmentInput } from '@/lib/api/generated/endpoints.schemas';
import { formatCost } from '@/lib/formatUnits';
import { cn } from '@/lib/utils';
import { NAMED_COLOR_SWATCHES } from '../lib/colorSwatches';
import {
  type AssignmentRow,
  matchesRow,
  rowLabel,
  useAssignmentRows,
} from './useAssignmentRows';

export interface FilamentVariantDraft {
  /** Set once saved; a saved variant's name and color are fixed. */
  id?: string;
  colorName: string;
  colorHex: string;
  pricePerKg: number | null;
}

export interface VariantDialogResult {
  variant: FilamentVariantDraft;
  /** Profile overrides for this color, keyed to `variant.colorHex`. */
  overrides: FilamentProfileAssignmentInput[];
}

function emptyVariant(): FilamentVariantDraft {
  const [first] = NAMED_COLOR_SWATCHES;
  return { colorName: first.name, colorHex: first.hex, pricePerKg: null };
}

const rowKey = (
  row: Pick<AssignmentRow, 'brand' | 'modelCode' | 'nozzleDiameterMm'>,
) => `${row.brand}|${row.modelCode}|${row.nozzleDiameterMm}`;

interface VariantDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The variant to edit; omitted to add one. */
  variant?: FilamentVariantDraft;
  /** The filament's price per kg, used when the variant has no price of its own. */
  inheritedPrice: number | null;
  materialType: string;
  /** Every assignment in the draft: defaults (no color) and all color overrides. */
  assignments: FilamentProfileAssignmentInput[];
  onSave: (result: VariantDialogResult) => void;
}

export function VariantDialog({
  open,
  onOpenChange,
  variant,
  inheritedPrice,
  materialType,
  assignments,
  onSave,
}: VariantDialogProps) {
  const [draft, setDraft] = useState<FilamentVariantDraft>(emptyVariant());
  // Row key -> profile id; a missing key inherits the row's default.
  const [overrides, setOverrides] = useState<Record<string, string>>({});
  const { fleet, rows } = useAssignmentRows(assignments);

  // Seed only when the dialog opens, so a background refetch can't reset in-progress edits.
  // biome-ignore lint/correctness/useExhaustiveDependencies: seed on open only
  useEffect(() => {
    if (!open) return;
    const existing = assignments.filter(
      (a) => variant && a.colorHex === variant.colorHex,
    );
    setDraft(variant ?? emptyVariant());
    setOverrides(
      Object.fromEntries(
        existing.map((a) => [rowKey(a), a.filamentProfileId ?? '']),
      ),
    );
  }, [open]);

  const isSaved = Boolean(draft.id);
  const priceInvalid = draft.pricePerKg != null && draft.pricePerKg < 0;
  const canSave = draft.colorName.trim().length > 0 && !priceInvalid;

  function setOverride(row: AssignmentRow, profileId: string | null) {
    setOverrides((prev) => {
      const next = { ...prev };
      if (profileId) next[rowKey(row)] = profileId;
      else delete next[rowKey(row)];
      return next;
    });
  }

  function handleSubmit(event: FormEvent) {
    // Also stops the submit from bubbling to an ancestor <form> in the React tree — this dialog
    // is rendered inside FilamentFormPage's own form, and React's portal event bubbling follows
    // the component tree, not the DOM tree, so an un-stopped submit here would also trigger the
    // outer form's onSubmit.
    event.preventDefault();
    event.stopPropagation();
    if (!canSave) return;
    onSave({
      variant: { ...draft, colorName: draft.colorName.trim() },
      overrides: rows
        .filter((row) => overrides[rowKey(row)])
        .map((row) => ({
          brand: row.brand,
          modelCode: row.modelCode,
          nozzleDiameterMm: row.nozzleDiameterMm,
          filamentProfileId: overrides[rowKey(row)],
          colorHex: draft.colorHex,
        })),
    });
  }

  const header = (
    <>
      <DialogTitle className="flex items-center gap-2">
        {variant && <Swatch colorHex={variant.colorHex} size="md" />}
        {variant ? `Edit ${variant.colorName}` : 'Add variant'}
      </DialogTitle>
      {!isSaved && (
        <DialogDescription>Color of this filament.</DialogDescription>
      )}
    </>
  );

  const footer = (
    <>
      <Button
        type="button"
        variant="outline"
        onClick={() => onOpenChange(false)}
      >
        Cancel
      </Button>
      <Button type="submit" form="variant-dialog-form" disabled={!canSave}>
        {variant ? 'Update variant' : 'Add variant'}
      </Button>
    </>
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogScrollableContent
        className="sm:max-w-lg"
        {...(isSaved ? { 'aria-describedby': undefined } : {})}
        header={header}
        footer={footer}
      >
        <form id="variant-dialog-form" onSubmit={handleSubmit} noValidate>
          <FieldGroup className="gap-6">
            {!isSaved && (
              <FieldGroup className="gap-4">
                <Field>
                  <FieldLabel htmlFor="variant-color-name">Name</FieldLabel>
                  <Input
                    id="variant-color-name"
                    value={draft.colorName}
                    onChange={(event) =>
                      setDraft((prev) => ({
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
                        aria-pressed={draft.colorHex === option.hex}
                        onClick={() =>
                          setDraft((prev) => ({
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
                          draft.colorHex === option.hex &&
                            'ring-2 ring-ring ring-offset-1 ring-offset-background',
                        )}
                      >
                        <Swatch colorHex={option.hex} size="lg" />
                      </button>
                    ))}
                  </div>
                </FieldSet>
              </FieldGroup>
            )}

            <FieldGroup className="gap-4">
              <Field data-invalid={priceInvalid || undefined}>
                <FieldLabel htmlFor="variant-price">Price per kg</FieldLabel>
                <Input
                  id="variant-price"
                  type="number"
                  min="0"
                  step="any"
                  aria-invalid={priceInvalid || undefined}
                  aria-describedby={
                    priceInvalid ? 'variant-price-error' : undefined
                  }
                  placeholder={
                    inheritedPrice == null
                      ? 'No default price'
                      : `Default: ${formatCost(inheritedPrice)}`
                  }
                  value={draft.pricePerKg ?? ''}
                  onChange={(event) =>
                    setDraft((prev) => ({
                      ...prev,
                      pricePerKg:
                        event.target.value === ''
                          ? null
                          : Number(event.target.value),
                    }))
                  }
                />
                {priceInvalid && (
                  <FieldError id="variant-price-error">
                    Price must be non-negative
                  </FieldError>
                )}
              </Field>

              {fleet && rows.length === 0 && (
                <p className="text-sm text-muted-foreground">
                  No printer nozzles reported.
                </p>
              )}
              {rows.map((row) => {
                const label = rowLabel(row);
                const id = `variant-profile-${rowKey(row)}`;
                const hasDefault = assignments.some(
                  (a) => matchesRow(a, row) && !a.colorHex,
                );
                const value = overrides[rowKey(row)];
                return (
                  <Field key={rowKey(row)}>
                    <FieldLabel htmlFor={id}>{label} profile</FieldLabel>
                    <div className="flex items-center gap-2">
                      <div className="min-w-0 flex-1">
                        <ProfilePickerCombobox
                          kind="filament"
                          id={id}
                          value={value}
                          onChange={(profileId) => setOverride(row, profileId)}
                          materialType={materialType}
                          brand={row.brand}
                          modelCode={row.modelCode}
                          nozzleDiameterMm={row.nozzleDiameterMm}
                          placeholder={
                            hasDefault
                              ? 'Use default profile'
                              : 'No default assigned'
                          }
                        />
                      </div>
                      {value && (
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Use default for ${label}`}
                          onClick={() => setOverride(row, null)}
                        >
                          <RotateCcwIcon />
                        </Button>
                      )}
                    </div>
                  </Field>
                );
              })}
            </FieldGroup>
          </FieldGroup>
        </form>
      </DialogScrollableContent>
    </Dialog>
  );
}
