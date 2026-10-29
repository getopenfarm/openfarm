import { useQuery } from '@tanstack/react-query';
import { createContext, type ReactNode, useContext } from 'react';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Skeleton } from '@/components/ui/skeleton';
import type { GeneralSettingsResponse } from '@/lib/api/generated/endpoints.schemas';
import { httpClient } from '@/lib/api/httpClient';
export const generalSettingsKey = '/api/v1/settings/general';
export type GeneralSettings = Required<GeneralSettingsResponse>;
export const useGeneralSettings = () =>
  useQuery({
    queryKey: [generalSettingsKey],
    queryFn: ({ signal }) =>
      httpClient<{ data: GeneralSettings }>(generalSettingsKey, { signal }),
  });
const CurrencyContext = createContext('USD');
export const useCurrency = () => useContext(CurrencyContext);
export function CurrencyProvider({ children }: { children: ReactNode }) {
  const query = useGeneralSettings();
  if (!query.data)
    return query.isError ? (
      <ErrorState
        title="Could not load general settings"
        onRetry={() => void query.refetch()}
      />
    ) : (
      <Skeleton className="h-40" />
    );
  return (
    <CurrencyContext.Provider value={query.data.data.currencyCode}>
      {children}
    </CurrencyContext.Provider>
  );
}
