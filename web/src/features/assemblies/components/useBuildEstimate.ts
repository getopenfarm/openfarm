import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import {
  getOpenFarmApiEndpointsAssembliesQuoteAssemblyEndpointUrl,
  openFarmApiEndpointsAssembliesQuoteAssemblyEndpoint,
} from '@/lib/api/generated/assemblies/assemblies';
import type {
  AssemblyCostInput,
  AssemblyQuote,
  QuoteAssemblyRequest,
} from '@/lib/api/generated/endpoints.schemas';
import type { EditableLine } from './BomLineEditor';

/** The pricing control's draft: the typed text is priced as typed, blank prices nothing. */
export interface PricingDraftInput {
  mode: string;
  value: string;
}

/** The request for a draft: picker rows without a target and mid-typing quantities are left out. */
export function quoteRequest(
  lines: EditableLine[],
  additionalCosts: AssemblyCostInput[],
  pricing?: PricingDraftInput,
): QuoteAssemblyRequest {
  const value =
    pricing && pricing.value.trim() !== '' ? Number(pricing.value) : NaN;
  return {
    lines: lines
      .filter((l) => l.targetId && Number.isFinite(l.quantity))
      .map((l) => ({
        targetKind: l.targetKind,
        targetId: l.targetId,
        quantity: l.quantity,
      })),
    additionalCosts,
    pricing:
      pricing && Number.isFinite(value) ? { mode: pricing.mode, value } : null,
  };
}

/**
 * Build totals and fee-profile prices for the draft, from POST /api/v1/assemblies/quote (dev-notes/PRICING.md). The Build
 * panel and Pricing share one request; edits wait 300 ms, and the last totals stay on screen while the next ones load.
 */
export function useBuildEstimate(
  lines: EditableLine[],
  additionalCosts: AssemblyCostInput[] = [],
  pricing?: PricingDraftInput,
) {
  const body = useDebouncedValue(
    JSON.stringify(quoteRequest(lines, additionalCosts, pricing)),
    300,
  );
  const query = useQuery({
    queryKey: [
      getOpenFarmApiEndpointsAssembliesQuoteAssemblyEndpointUrl(),
      body,
    ],
    queryFn: async ({ signal }) => {
      const response =
        await openFarmApiEndpointsAssembliesQuoteAssemblyEndpoint(
          JSON.parse(body),
          { signal },
        );
      return response.data as AssemblyQuote;
    },
    placeholderData: keepPreviousData,
  });
  const quote = query.data;
  const loading = query.isPending;
  const failed = query.isError && !quote;
  return {
    quote,
    loading,
    failed,
    /** No totals to show yet. */
    incomplete: loading || failed,
    fetching: query.isFetching,
    retry: () => void query.refetch(),
  };
}

export type BuildEstimate = ReturnType<typeof useBuildEstimate>;
