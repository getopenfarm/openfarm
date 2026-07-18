import type {
  FilamentResponse,
  ListFilamentsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint } from '@/lib/api/generated/filaments/filaments';

export function useFilamentCatalog(): FilamentResponse[] {
  const filamentsQuery =
    useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint();
  return (
    (filamentsQuery.data?.data as ListFilamentsResponse | undefined)
      ?.filaments ?? []
  );
}
