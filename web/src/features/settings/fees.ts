import { useQuery } from '@tanstack/react-query';
import type { FeesResponse } from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
import { formatCost } from '@/lib/formatUnits';

export const feesKey = '/api/v1/settings/fees';
export function useFeeProfiles() {
  return useQuery({
    queryKey: [feesKey],
    queryFn: ({ signal }) =>
      httpClient<{ data: FeesResponse }>(feesKey, { signal }),
  });
}

export function feeSummary(percent: number, fixed: number, currency: string) {
  const parts = [];
  if (percent > 0) parts.push(`${Number(percent.toFixed(2))}% of sale price`);
  if (fixed > 0) parts.push(`${formatCost(fixed, currency)} per order`);
  return parts.join(' + ') || 'No fees';
}
