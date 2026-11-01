import { useQuery } from '@tanstack/react-query';
import type { PrintEstimate } from '@/lib/api/generated/endpoints.schemas';
import { openFarmApiEndpointsPartsEstimatePrintedPartsEndpoint } from '@/lib/api/generated/parts/parts';

/** Draft BOM quantities, not production demand: an estimate must not require slicing readiness or save the draft. */
export function usePrintedPartsEstimate(
  parts: Map<string, number>,
  enabled: boolean,
) {
  const inputs = [...parts]
    .filter(([, quantity]) => quantity > 0)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([partId, quantity]) => ({ partId, quantity }));
  return useQuery({
    queryKey: ['/api/v1/parts/print-estimate', inputs],
    queryFn: async ({ signal }) => {
      const result =
        await openFarmApiEndpointsPartsEstimatePrintedPartsEndpoint(
          { parts: inputs },
          { signal },
        );
      const data = result.data as PrintEstimate;
      if (!data?.time || !data.filament || !Array.isArray(data.colors))
        throw new Error('Could not read print estimates.');
      return data;
    },
    enabled,
  });
}
