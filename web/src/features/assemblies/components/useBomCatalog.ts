import { useQuery } from '@tanstack/react-query';
import { useAllCostItems } from '@/features/cost-catalog/api';
import type {
  AssemblySummaryResponse,
  PartSummaryResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';

/** The editor needs complete collections; every API page uses the supported 200-row limit. */
function useCompleteCollection<T>(path: string, itemKey: 'parts' | 'items') {
  return useQuery({
    queryKey: [path, { catalog: true }],
    queryFn: async ({ signal }) => {
      const rows: T[] = [];
      let page = 1;
      while (true) {
        const response = await httpClient<{
          data: { parts?: T[]; items?: T[]; totalCount?: number };
        }>(`${path}?page=${page}&pageSize=200`, { signal });
        const items = response.data[itemKey] ?? [];
        rows.push(...items);
        if (rows.length >= (response.data.totalCount ?? rows.length))
          return rows;
        if (items.length === 0)
          throw new Error('Catalog page was empty before all records loaded.');
        page++;
      }
    },
  });
}

/** Shared query prefixes preserve invalidation after part, assembly and hardware edits. */
export function useBomCatalog(excludeAssemblyId?: string) {
  const partsQuery = useCompleteCollection<PartSummaryResponse>(
    '/api/v1/parts',
    'parts',
  );
  const assembliesQuery = useCompleteCollection<AssemblySummaryResponse>(
    '/api/v1/assemblies',
    'items',
  );
  const componentsQuery = useAllCostItems();
  const parts = partsQuery.data ?? [];
  const assemblies = (assembliesQuery.data ?? []).filter(
    (a) => a.id !== excludeAssemblyId,
  );
  const costItems = componentsQuery.data ?? [];
  const components = costItems
    .filter((c) => c.pricing?.itemType !== 'Service')
    .map((c) => ({ ...c, unitCost: c.effectiveRate }));
  const queries = [partsQuery, assembliesQuery, componentsQuery];
  return {
    parts,
    assemblies,
    components,
    costItems,
    isPending: queries.some((q) => q.isPending),
    isFetching: queries.some((q) => q.isFetching),
    isError: queries.some((q) => q.isError),
    retry: () => {
      for (const query of queries) void query.refetch();
    },
    thumbnailFor: (id: string) =>
      parts.find((p) => p.id === id)?.thumbnailBase64 ?? null,
  };
}

export type BomCatalog = ReturnType<typeof useBomCatalog>;
