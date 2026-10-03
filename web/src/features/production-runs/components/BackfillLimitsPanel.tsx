import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { extractErrorDetail } from '@/lib/api/extractErrorDetail';
import {
  getOpenFarmApiEndpointsProductionRunsGetProductionRunEndpointQueryKey,
  useOpenFarmApiEndpointsProductionRunsSetRunLineBackfillLimitsEndpoint,
} from '@/lib/api/generated/production-runs/production-runs';

interface BackfillLimitsPanelProps {
  runId: string;
  lineId: string;
  backfillPrinterCap: number;
  backfillCumulativeUnitCeiling: number | null | undefined;
  cumulativeProducedUnits: number;
}

/**
 * Printer-cap and cumulative-ceiling editor for an unlimited (backfill) line — blank ceiling means
 * no ceiling (spec Assumptions' off-by-default). Progress is always a cumulative produced count,
 * never a completion percentage — an unlimited line has no denominator to compute one against
 * (FR-006e).
 */
export function BackfillLimitsPanel({
  runId,
  lineId,
  backfillPrinterCap,
  backfillCumulativeUnitCeiling,
  cumulativeProducedUnits,
}: BackfillLimitsPanelProps) {
  const queryClient = useQueryClient();
  const [printerCap, setPrinterCap] = useState(String(backfillPrinterCap));
  const [ceiling, setCeiling] = useState(
    backfillCumulativeUnitCeiling != null
      ? String(backfillCumulativeUnitCeiling)
      : '',
  );

  const mutation =
    useOpenFarmApiEndpointsProductionRunsSetRunLineBackfillLimitsEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Backfill limits updated.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsProductionRunsGetProductionRunEndpointQueryKey(
                runId,
              ),
          });
        },
        onError: async (error) => {
          toast.error(
            await extractErrorDetail(
              error,
              'Could not update backfill limits.',
            ),
          );
        },
      },
    });

  function handleSave() {
    const cap = Number(printerCap);
    if (Number.isNaN(cap) || cap < 1) {
      toast.error('Printer cap must be a whole number of at least 1.');
      return;
    }

    const ceilingValue = ceiling.trim() === '' ? null : Number(ceiling);
    if (
      ceilingValue !== null &&
      (Number.isNaN(ceilingValue) || ceilingValue < 0)
    ) {
      toast.error(
        'Cumulative ceiling must be 0 or more, or blank for no ceiling.',
      );
      return;
    }

    mutation.mutate({
      runId,
      lineId,
      data: {
        backfillPrinterCap: cap,
        backfillCumulativeUnitCeiling: ceilingValue,
      },
    });
  }

  return (
    <div className="flex flex-col gap-2 rounded-md border p-3">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium">Backfill limits</span>
        <span className="text-xs text-muted-foreground">
          {cumulativeProducedUnits} produced so far
        </span>
      </div>
      <div className="flex items-end gap-2">
        <div className="flex flex-col gap-1">
          <Label htmlFor={`backfill-cap-${lineId}`}>Printer cap</Label>
          <Input
            id={`backfill-cap-${lineId}`}
            type="number"
            min={1}
            step={1}
            value={printerCap}
            onChange={(e) => setPrinterCap(e.target.value)}
            className="w-24"
          />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor={`backfill-ceiling-${lineId}`}>
            Cumulative ceiling
          </Label>
          <Input
            id={`backfill-ceiling-${lineId}`}
            type="number"
            min={0}
            step={1}
            placeholder="No ceiling"
            value={ceiling}
            onChange={(e) => setCeiling(e.target.value)}
            className="w-32"
          />
        </div>
        <Button
          type="button"
          size="sm"
          disabled={mutation.isPending}
          onClick={handleSave}
        >
          Save
        </Button>
      </div>
    </div>
  );
}
