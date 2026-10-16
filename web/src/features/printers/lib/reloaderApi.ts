import type { ConfigureReloaderRequest } from '@/lib/api/generated/endpoints.schemas';
import {
  openFarmApiEndpointsFeedUnitsConfigureReloaderEndpoint,
  openFarmApiEndpointsFeedUnitsSelectReloaderSlotEndpoint,
} from '@/lib/api/generated/feed-units/feed-units';

export function configureReloader(id: string, data: ConfigureReloaderRequest) {
  return openFarmApiEndpointsFeedUnitsConfigureReloaderEndpoint(id, data);
}

export function selectReloaderSlot(id: string, feedingSlotId: string) {
  return openFarmApiEndpointsFeedUnitsSelectReloaderSlotEndpoint(id, {
    feedingSlotId,
  });
}
