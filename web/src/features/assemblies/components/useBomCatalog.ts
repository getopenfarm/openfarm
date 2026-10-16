import { useOpenFarmApiEndpointsAssembliesListAssembliesEndpoint } from '@/lib/api/generated/assemblies/assemblies';
import type {
  ListAssembliesResponse,
  ListNonPrintedComponentsResponse,
  ListPartsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpoint } from '@/lib/api/generated/non-printed-components/non-printed-components';
import { useOpenFarmApiEndpointsPartsListPartsEndpoint } from '@/lib/api/generated/parts/parts';

/** Parts (thumbnails, estimates), assemblies, and components (cost) for the editor and build preview. Shared query keys, so one fetch each. */
export function useBomCatalog(excludeAssemblyId?: string) {
  const partsQuery = useOpenFarmApiEndpointsPartsListPartsEndpoint({
    page: 1,
    pageSize: 500,
  });
  const assembliesQuery =
    useOpenFarmApiEndpointsAssembliesListAssembliesEndpoint({
      page: 1,
      pageSize: 500,
    });
  const componentsQuery =
    useOpenFarmApiEndpointsNonPrintedComponentsListNonPrintedComponentsEndpoint(
      { page: 1, pageSize: 500 },
    );

  const parts =
    (partsQuery.data?.data as ListPartsResponse | undefined)?.parts ?? [];
  const assemblies = (
    (assembliesQuery.data?.data as ListAssembliesResponse | undefined)?.items ??
    []
  ).filter((a) => a.id !== excludeAssemblyId);
  const components =
    (componentsQuery.data?.data as ListNonPrintedComponentsResponse | undefined)
      ?.items ?? [];

  return {
    parts,
    assemblies,
    components,
    thumbnailFor: (id: string) =>
      parts.find((p) => p.id === id)?.thumbnailBase64 ?? null,
  };
}

export type BomCatalog = ReturnType<typeof useBomCatalog>;
