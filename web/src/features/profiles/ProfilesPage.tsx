import { useState } from 'react';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import type {
  FilamentProfileDetailResponse,
  ListFilamentProfilesResponse,
  ListMachineProfilesResponse,
  MachineProfileDetailResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsProfilesGetFilamentProfileEndpoint,
  useOpenFarmApiEndpointsProfilesGetMachineProfileEndpoint,
  useOpenFarmApiEndpointsProfilesListFilamentProfilesEndpoint,
  useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint,
} from '@/lib/api/generated/profiles/profiles';
import { FilamentProfilesTable } from './components/FilamentProfilesTable';
import { ImportProfilesDialog } from './components/ImportProfilesDialog';
import { MachineProfilesTable } from './components/MachineProfilesTable';
import { ProfileRawDataDialog } from './components/ProfileRawDataDialog';

type SelectedProfile = { type: 'Machine' | 'Filament'; id: string } | null;

export function ProfilesPage() {
  const [selected, setSelected] = useState<SelectedProfile>(null);

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

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="font-heading text-2xl font-semibold">Profiles</h2>
          <p className="text-sm text-muted-foreground">
            Machine and filament profiles imported from slicer bundles
          </p>
        </div>
        <ImportProfilesDialog />
      </div>
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
      />
    </div>
  );
}
