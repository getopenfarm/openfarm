import { useQuery } from '@tanstack/react-query';
import type {
  CostItemResponse,
  ListCostItemsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
export const catalogKey = '/api/v1/cost-items';
export function useAllCostItems() {
  return useQuery({
    queryKey: [catalogKey, { complete: true }],
    queryFn: async ({ signal }) => {
      const items: CostItemResponse[] = [];
      for (let page = 1; ; page++) {
        const result = await httpClient<{ data: ListCostItemsResponse }>(
          `${catalogKey}?page=${page}&pageSize=200`,
          { signal },
        );
        const rows = result.data.items ?? [];
        items.push(...rows);
        if (items.length >= (result.data.totalCount ?? items.length))
          return items;
        if (rows.length === 0)
          throw new Error('Catalog page was empty before all items loaded.');
      }
    },
  });
}
