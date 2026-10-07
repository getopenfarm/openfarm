import type { ConfigureReloaderRequest } from '@/lib/api/generated/endpoints.schemas';
import {
  openFarmApiEndpointsFeedUnitsConfigureReloaderEndpoint,
  openFarmApiEndpointsFeedUnitsSelectReloaderChannelEndpoint,
} from '@/lib/api/generated/feed-units/feed-units';

export function configureReloader(id: string, data: ConfigureReloaderRequest) {
  return openFarmApiEndpointsFeedUnitsConfigureReloaderEndpoint(id, data);
}

export function selectReloaderChannel(id: string, feedingSlotId: string) {
  return openFarmApiEndpointsFeedUnitsSelectReloaderChannelEndpoint(id, {
    feedingSlotId,
  });
}
