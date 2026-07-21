import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type {
  ListProcessProfilesResponse,
  ProcessProfileResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsProfilesGetMachineProfileEndpointQueryKey,
  useOpenFarmApiEndpointsProfilesListProcessProfilesEndpoint,
  useOpenFarmApiEndpointsProfilesSetMachineProfileDefaultProcessEndpoint,
} from '@/lib/api/generated/profiles/profiles';

export interface MachineDefaultProcessSelectProps {
  machineId: string;
  machineSourceFormat: string;
  defaultProcessProfileId: string | null | undefined;
}

/**
 * Pairs a ProcessProfile to a specific MachineProfile — Orca/Bambu Studio enforce strict
 * machine/process compatibility at slice time (only a preset authored/shipped for the exact
 * machine passes), so a process preset is never a fixed generic default; it's always paired
 * explicitly per machine. Filters the process list to the same source format as a client-side UX
 * convenience — the backend still authoritatively rejects a cross-format pairing with 422.
 */
export function MachineDefaultProcessSelect({
  machineId,
  machineSourceFormat,
  defaultProcessProfileId,
}: MachineDefaultProcessSelectProps) {
  const queryClient = useQueryClient();
  const processQuery =
    useOpenFarmApiEndpointsProfilesListProcessProfilesEndpoint();
  const allProcesses =
    (processQuery.data?.data as ListProcessProfilesResponse | undefined)
      ?.profiles ?? ([] as ProcessProfileResponse[]);
  const processes = allProcesses.filter(
    (profile) => profile.sourceFormat === machineSourceFormat,
  );

  const pairMutation =
    useOpenFarmApiEndpointsProfilesSetMachineProfileDefaultProcessEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Process profile paired.');
          queryClient.invalidateQueries({
            queryKey:
              getOpenFarmApiEndpointsProfilesGetMachineProfileEndpointQueryKey(
                machineId,
              ),
          });
        },
        onError: () => toast.error('Could not pair this process profile.'),
      },
    });

  function handleChange(value: string) {
    pairMutation.mutate({ id: machineId, data: { processProfileId: value } });
  }

  if (!processQuery.isLoading && processes.length === 0) {
    return (
      <p className="text-xs text-muted-foreground">
        No {machineSourceFormat} process profiles imported yet — import one to
        pair it with this machine.
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-1.5">
      <Select
        value={defaultProcessProfileId ?? undefined}
        onValueChange={handleChange}
        disabled={pairMutation.isPending || processQuery.isLoading}
      >
        <SelectTrigger className="w-full">
          <SelectValue placeholder="No process profile paired" />
        </SelectTrigger>
        <SelectContent>
          {processes.map((profile) => (
            <SelectItem key={profile.id} value={profile.id ?? ''}>
              {profile.name}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      {!defaultProcessProfileId && (
        <p className="text-xs text-muted-foreground">
          No process profile paired — this machine can't be set as the
          organization's default until one is.
        </p>
      )}
    </div>
  );
}
