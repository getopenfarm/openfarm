import { modelKey } from '@/features/settings/printerCosts';
import type {
  PartSummaryResponse,
  PrinterModelCostResponse,
} from '@/lib/api/generated/endpoints.schemas';

/** Model hourly rates are calculated by the server; draft quantities allocate fractional plates. */
export function machineCosts(
  parts: Map<string, number>,
  catalog: PartSummaryResponse[],
  models: PrinterModelCostResponse[],
) {
  const depreciation = { total: 0, priced: 0, missing: 0 };
  const electricity = { total: 0, priced: 0, missing: 0 };
  for (const [id, quantity] of parts) {
    const part = catalog.find((p) => p.id === id);
    const model =
      part?.estimationPrinterBrand && part.estimationPrinterModelCode
        ? models.find(
            (m) =>
              modelKey(m.brand, m.modelCode) ===
              modelKey(
                part.estimationPrinterBrand ?? '',
                part.estimationPrinterModelCode ?? '',
              ),
          )
        : undefined;
    const hours =
      part?.estimatedPrintTimeMinutes == null
        ? null
        : ((quantity / Math.max(1, part.unitsPerPrint ?? 1)) *
            part.estimatedPrintTimeMinutes) /
          60;
    for (const [result, rate] of [
      [depreciation, model?.depreciationPerHour],
      [electricity, model?.electricityPerHour],
    ] as const) {
      if (hours == null || rate == null) result.missing++;
      else {
        result.total += hours * rate;
        result.priced++;
      }
    }
  }
  return {
    depreciation,
    electricity,
    total: depreciation.total + electricity.total,
    incomplete: depreciation.missing + electricity.missing > 0,
  };
}
