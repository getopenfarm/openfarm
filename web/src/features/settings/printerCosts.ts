import { type QueryClient, useQuery } from '@tanstack/react-query';
import type {
  PrinterCatalogResponse,
  PrinterCostsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';

export const printerCostsKey = '/api/v1/settings/printer-costs';
export const modelKey = (brand?: string, modelCode?: string) =>
  `${brand}/${modelCode}`;
export function usePrinterCosts() {
  return useQuery({
    queryKey: [printerCostsKey],
    queryFn: ({ signal }) =>
      httpClient<{ data: PrinterCostsResponse }>(printerCostsKey, { signal }),
  });
}
export function usePrinterCostCatalog() {
  return useQuery({
    queryKey: ['/api/v1/printers/catalog'],
    queryFn: ({ signal }) =>
      httpClient<{ data: PrinterCatalogResponse }>('/api/v1/printers/catalog', {
        signal,
      }),
  });
}
export function invalidatePrinterCosts(client: QueryClient) {
  void client.invalidateQueries({ queryKey: [printerCostsKey] });
  void client.invalidateQueries({
    predicate: (q) => String(q.queryKey[0]).startsWith('/api/v1/assemblies'),
  });
}
