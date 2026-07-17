import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { toast } from 'sonner';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import type {
  FilamentProfileDetailResponse,
  ListFilamentProfilesResponse,
  ListMachineProfilesResponse,
  MachineProfileDetailResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsProfilesGetFilamentProfileEndpointQueryKey,
  getOpenFarmApiEndpointsProfilesGetMachineProfileEndpointQueryKey,
  getOpenFarmApiEndpointsProfilesListFilamentProfilesEndpointQueryKey,
  getOpenFarmApiEndpointsProfilesListMachineProfilesEndpointQueryKey,
  useOpenFarmApiEndpointsProfilesGetFilamentProfileEndpoint,
  useOpenFarmApiEndpointsProfilesGetMachineProfileEndpoint,
  useOpenFarmApiEndpointsProfilesListFilamentProfilesEndpoint,
  useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint,
  useOpenFarmApiEndpointsProfilesUpdateFilamentProfileRawDataEndpoint,
  useOpenFarmApiEndpointsProfilesUpdateMachineProfileRawDataEndpoint,
} from '@/lib/api/generated/profiles/profiles';
import { FilamentProfilesTable } from './components/FilamentProfilesTable';
import { ImportProfilesDialog } from './components/ImportProfilesDialog';
import { MachineProfilesTable } from './components/MachineProfilesTable';
import { ProfileRawDataDialog } from './components/ProfileRawDataDialog';

type SelectedProfile = { type: 'Machine' | 'Filament'; id: string } | null;

export function ProfilesPage() {
  const [selected, setSelected] = useState<SelectedProfile>(null);
  const queryClient = useQueryClient();

  const machineListQuery =
    useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint();
  const filamentListQuery =
    useOpenFarmApiEndpointsProfilesListFilamentProfilesEndpoint();

  const machineDetailQuery =
    useOpenFarmApiEndpointsProfilesGetMachineProfileEndpoint(
      selected?.type === 'Machine' ? selected.id : '',
      { query: { enabled: selected?.type === 'Machine' } },
    );
  const filamentDetailQuery =
    useOpenFarmApiEndpointsProfilesGetFilamentProfileEndpoint(
      selected?.type === 'Filament' ? selected.id : '',
      { query: { enabled: selected?.type === 'Filament' } },
    );

  const machineProfiles =
    (machineListQuery.data?.data as ListMachineProfilesResponse | undefined)
      ?.profiles ?? [];
  const filamentProfiles =
    (filamentListQuery.data?.data as ListFilamentProfilesResponse | undefined)
      ?.profiles ?? [];

  const detailQuery =
    selected?.type === 'Machine' ? machineDetailQuery : filamentDetailQuery;
  const detail = detailQuery.data?.data as
    | MachineProfileDetailResponse
    | FilamentProfileDetailResponse
    | undefined;

  const updateMachineMutation =
    useOpenFarmApiEndpointsProfilesUpdateMachineProfileRawDataEndpoint();
  const updateFilamentMutation =
    useOpenFarmApiEndpointsProfilesUpdateFilamentProfileRawDataEndpoint();

  async function handleSaveRawData(rawDataJson: string) {
    if (!selected) {
      return;
    }

    try {
      if (selected.type === 'Machine') {
        await updateMachineMutation.mutateAsync({
          id: selected.id,
          data: { rawDataJson },
        });
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsProfilesGetMachineProfileEndpointQueryKey(
              selected.id,
            ),
        });
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsProfilesListMachineProfilesEndpointQueryKey(),
        });
      } else {
        await updateFilamentMutation.mutateAsync({
          id: selected.id,
          data: { rawDataJson },
        });
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsProfilesGetFilamentProfileEndpointQueryKey(
              selected.id,
            ),
        });
        queryClient.invalidateQueries({
          queryKey:
            getOpenFarmApiEndpointsProfilesListFilamentProfilesEndpointQueryKey(),
        });
      }
      toast.success('Profile updated.');
    } catch (error) {
      if (error instanceof Response && error.status === 422) {
        const problem = await error.json();
        throw new Error(problem.detail ?? 'The raw data is not valid JSON.');
      }
      throw new Error('Could not save this profile.');
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Profiles"
        description="Machine and filament profiles imported from slicer bundles"
        action={<ImportProfilesDialog />}
      />
      <Tabs defaultValue="machine">
        <TabsList>
          <TabsTrigger value="machine">Machine</TabsTrigger>
          <TabsTrigger value="filament">Filament</TabsTrigger>
        </TabsList>
        <TabsContent value="machine">
          <MachineProfilesTable
            profiles={machineProfiles}
            onRowClick={(id) => setSelected({ type: 'Machine', id })}
          />
        </TabsContent>
        <TabsContent value="filament">
          <FilamentProfilesTable
            profiles={filamentProfiles}
            onRowClick={(id) => setSelected({ type: 'Filament', id })}
          />
        </TabsContent>
      </Tabs>
      <ProfileRawDataDialog
        open={selected !== null}
        onOpenChange={(open) => {
          if (!open) {
            setSelected(null);
          }
        }}
        name={detail?.name}
        rawDataJson={detail?.rawDataJson}
        isLoading={detailQuery.isLoading}
        onSave={handleSaveRawData}
        isSaving={
          updateMachineMutation.isPending || updateFilamentMutation.isPending
        }
      />
    </div>
  );
}
