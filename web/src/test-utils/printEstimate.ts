import type {
  PartSummaryResponse,
  PrintEstimate,
} from '@/lib/api/generated/endpoints.schemas';

/** Fake server response for existing UI scenarios. Calculator correctness is tested against the real backend. */
export function printEstimateFixture(
  inputs: { partId: string; quantity: number }[],
  catalog: PartSummaryResponse[],
): PrintEstimate {
  const parts = inputs
    .filter((input) => input.quantity > 0)
    .map((input) => {
      const part = catalog.find((item) => item.id === input.partId);
      const scale = input.quantity / (part?.unitsPerPrint ?? 1);
      const colors = part?.filamentByColor ?? [];
      const weights =
        colors.length > 0 && colors.every((color) => color.grams != null)
          ? colors
          : part?.estimatedFilamentWeightGrams != null
            ? [
                {
                  ...(colors.length === 1 ? colors[0] : {}),
                  grams: part.estimatedFilamentWeightGrams,
                },
              ]
            : colors.length > 0
              ? colors
              : [{ grams: null }];
      const rows = weights.map((color) => ({
        materialType: color.materialType,
        colorHex: color.colorHex,
        variantId: color.narrowFilamentVariantId,
        grams: color.grams == null ? null : color.grams * scale,
        missing: color.grams == null ? 1 : 0,
      }));
      return {
        time: {
          value:
            part?.estimatedPrintTimeMinutes == null
              ? null
              : part.estimatedPrintTimeMinutes * scale,
          missing: part?.estimatedPrintTimeMinutes == null ? 1 : 0,
        },
        filament: {
          value: rows.some((row) => row.grams != null)
            ? rows.reduce((sum, row) => sum + (row.grams ?? 0), 0)
            : null,
          missing: rows.filter((row) => row.grams == null).length,
        },
        approximate: input.quantity % (part?.unitsPerPrint ?? 1) !== 0,
        colors: rows,
      };
    });
  const metric = (key: 'time' | 'filament') => ({
    value:
      parts.length === 0
        ? 0
        : parts.some((part) => part[key].value != null)
          ? parts.reduce((sum, part) => sum + (part[key].value ?? 0), 0)
          : null,
    missing: parts.reduce((sum, part) => sum + part[key].missing, 0),
  });
  const colors = new Map<
    string,
    NonNullable<PrintEstimate['colors']>[number]
  >();
  for (const color of parts.flatMap((part) => part.colors)) {
    const key = color.variantId ?? `${color.materialType}|${color.colorHex}`;
    const previous = colors.get(key);
    colors.set(key, {
      ...color,
      grams: previous
        ? previous.grams == null && color.grams == null
          ? null
          : (previous.grams ?? 0) + (color.grams ?? 0)
        : color.grams,
      missing: (previous?.missing ?? 0) + color.missing,
    });
  }
  return {
    time: metric('time'),
    filament: metric('filament'),
    approximate: parts.some((part) => part.approximate),
    colors: [...colors.values()],
  };
}
