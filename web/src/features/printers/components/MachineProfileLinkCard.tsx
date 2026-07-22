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
  ListProcessProfilesResponse,
  MachineProfileResponse,
  ProcessProfileResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey,
  useOpenFarmApiEndpointsPrintersEditPrinterEndpoint,
} from '@/lib/api/generated/printers/printers';
import {
  useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint,
  useOpenFarmApiEndpointsProfilesListProcessProfilesEndpoint,
} from '@/lib/api/generated/profiles/profiles';

const UNLINKED_VALUE = '__unlinked__';

export interface MachineProfileLinkCardProps {
  printerId: string;
  printerName: string;
  machineProfileId: string | null | undefined;
  processProfileId: string | null | undefined;
}

/**
 * Links a printer to its Machine and Process profile — the only source of nozzle diameter for
 * NozzleDiameterResolver (FR-005a/FR-005b) and, together, the slicing settings a fleet-driven or
 * printer-targeted slice resolves to (contracts/profiles-api.md). Saves through the consolidated
 * `PATCH /api/v1/printers/{id}` endpoint (FR-006/FR-007) rather than the removed dedicated
 * link endpoint.
 *
 * Only shows User-imported profiles today — picking from the System-ingested catalog (US2's full
 * scope, SC-008/SC-010) needs the searchable `ProfilePickerCombobox` (tasks.md T058) wired in once
 * the Orval client is regenerated against the extended list endpoints (T057); until then this is a
 * reduced-scope stand-in that at least targets the right (consolidated) save endpoint.
 */
export function MachineProfileLinkCard({
  printerId,
  printerName,
  machineProfileId,
  processProfileId,
}: MachineProfileLinkCardProps) {
  const queryClient = useQueryClient();

  const machineProfilesQuery =
    useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint();
  const machineProfiles =
    (machineProfilesQuery.data?.data as ListMachineProfilesResponse | undefined)
      ?.items ?? ([] as MachineProfileResponse[]);

  const processProfilesQuery =
    useOpenFarmApiEndpointsProfilesListProcessProfilesEndpoint();
  const processProfiles =
    (processProfilesQuery.data?.data as ListProcessProfilesResponse | undefined)
      ?.items ?? ([] as ProcessProfileResponse[]);

  const editMutation = useOpenFarmApiEndpointsPrintersEditPrinterEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Printer profiles updated.');
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsPrintersGetPrinterDetailEndpointQueryKey(
              printerId,
            ),
        });
      },
      onError: () => toast.error('Could not update the printer profiles.'),
    },
  });

  function handleMachineChange(value: string) {
    const nextMachineProfileId = value === UNLINKED_VALUE ? null : value;
    editMutation.mutate({
      id: printerId,
      data: {
        name: printerName,
        machineProfileId: nextMachineProfileId,
        // Clearing the machine profile clears the process profile too (FR-007) — a process
        // selection can never outlive the machine it was validated against.
        processProfileId: nextMachineProfileId ? processProfileId : null,
      },
    });
  }

  function handleProcessChange(value: string) {
    editMutation.mutate({
      id: printerId,
      data: {
        name: printerName,
        machineProfileId,
        processProfileId: value === UNLINKED_VALUE ? null : value,
      },
    });
  }

  return (
    <Card className="gap-0 py-0">
      <CardHeader className="border-b px-[18px] py-3.5">
        <CardTitle className="text-sm font-semibold">
          Machine &amp; process profile
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3 px-[18px] py-3.5">
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">
            Machine profile
          </span>
          <Select
            value={machineProfileId ?? UNLINKED_VALUE}
            onValueChange={handleMachineChange}
            disabled={editMutation.isPending || machineProfilesQuery.isLoading}
          >
            <SelectTrigger className="w-full">
              <SelectValue placeholder="No machine profile linked" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={UNLINKED_VALUE}>None</SelectItem>
              {machineProfiles.map((profile) => (
                <SelectItem key={profile.id} value={profile.id ?? ''}>
                  {profile.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="flex flex-col gap-1">
          <span className="text-xs text-muted-foreground">
            Process profile
          </span>
          <Select
            value={processProfileId ?? UNLINKED_VALUE}
            onValueChange={handleProcessChange}
            disabled={
              !machineProfileId ||
              editMutation.isPending ||
              processProfilesQuery.isLoading
            }
          >
            <SelectTrigger className="w-full">
              <SelectValue
                placeholder={
                  machineProfileId
                    ? 'No process profile linked'
                    : 'Select a machine profile first'
                }
              />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={UNLINKED_VALUE}>None</SelectItem>
              {processProfiles.map((profile) => (
                <SelectItem key={profile.id} value={profile.id ?? ''}>
                  {profile.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
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
