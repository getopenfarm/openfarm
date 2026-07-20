import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type {
  ListMachineProfilesResponse,
  MachineProfileResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersLinkPrinterMachineProfileEndpoint,
} from '@/lib/api/generated/printers/printers';
import { useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint } from '@/lib/api/generated/profiles/profiles';

const UNLINKED_VALUE = '__unlinked__';

export interface MachineProfileLinkCardProps {
  printerId: string;
  machineProfileId: string | null | undefined;
}

/**
 * Links a printer to an imported Machine Profile — the only source of nozzle diameter for
 * NozzleDiameterResolver (FR-005a/FR-005b). Without this link, requires-slicing part uploads
 * (STL, unsliced 3MF) can never resolve a nozzle diameter and are rejected outright at Add time.
 */
export function MachineProfileLinkCard({
  printerId,
  machineProfileId,
}: MachineProfileLinkCardProps) {
  const queryClient = useQueryClient();
  const profilesQuery =
    useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint();
  const profiles =
    (profilesQuery.data?.data as ListMachineProfilesResponse | undefined)
      ?.profiles ?? ([] as MachineProfileResponse[]);

  const linkMutation =
    useOpenFarmApiEndpointsPrintersLinkPrinterMachineProfileEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Machine profile updated.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey(
                printerId,
              ),
          });
        },
        onError: () => toast.error('Could not update the machine profile.'),
      },
    });

  function handleChange(value: string) {
    linkMutation.mutate({
      id: printerId,
      data: { machineProfileId: value === UNLINKED_VALUE ? null : value },
    });
  }

  return (
    <Card className="gap-0 py-0">
      <CardHeader className="border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">Machine profile</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-2 px-[18px] py-3.5">
        <Select
          value={machineProfileId ?? UNLINKED_VALUE}
          onValueChange={handleChange}
          disabled={linkMutation.isPending || profilesQuery.isLoading}
        >
          <SelectTrigger className="w-full">
            <SelectValue placeholder="No machine profile linked" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={UNLINKED_VALUE}>None</SelectItem>
            {profiles.map((profile) => (
              <SelectItem key={profile.id} value={profile.id ?? ''}>
                {profile.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        {!machineProfileId && (
          <p className="text-xs text-muted-foreground">
            No profile linked — this printer won't count toward the farm's
            auto-detected nozzle diameter, so uploads of files that need slicing
            (STL, unsliced 3MF) may be rejected if no other printer provides
            one.
          </p>
        )}
      </CardContent>
    </Card>
  );
}
