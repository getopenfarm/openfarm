import { useQueryClient } from '@tanstack/react-query';
import { LayersIcon, SettingsIcon, SwatchBookIcon } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import type {
  FilamentProfileDetailResponse,
  ListFilamentProfilesResponse,
  ListMachineProfilesResponse,
  ListProcessProfilesResponse,
  MachineProfileDetailResponse,
  ProcessProfileDetailResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  getOpenFarmApiEndpointsProfilesGetFilamentProfileEndpointQueryKey,
  getOpenFarmApiEndpointsProfilesGetMachineProfileEndpointQueryKey,
  getOpenFarmApiEndpointsProfilesListFilamentProfilesEndpointQueryKey,
  getOpenFarmApiEndpointsProfilesListMachineProfilesEndpointQueryKey,
  useOpenFarmApiEndpointsProfilesGetFilamentProfileEndpoint,
  useOpenFarmApiEndpointsProfilesGetMachineProfileEndpoint,
  useOpenFarmApiEndpointsProfilesGetProcessProfileEndpoint,
  useOpenFarmApiEndpointsProfilesListFilamentProfilesEndpoint,
  useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint,
  useOpenFarmApiEndpointsProfilesListProcessProfilesEndpoint,
  useOpenFarmApiEndpointsProfilesUpdateFilamentProfileRawDataEndpoint,
  useOpenFarmApiEndpointsProfilesUpdateMachineProfileRawDataEndpoint,
} from '@/lib/api/generated/profiles/profiles';
import { FilamentProfilesTable } from './components/FilamentProfilesTable';
import { ImportProfilesDialog } from './components/ImportProfilesDialog';
import { MachineProfilesTable } from './components/MachineProfilesTable';
import { ProcessProfilesTable } from './components/ProcessProfilesTable';
import { ProfileRawDataDialog } from './components/ProfileRawDataDialog';

type SelectedProfile = {
  type: 'Machine' | 'Filament' | 'Process';
  id: string;
} | null;

export function ProfilesPage() {
  const [selected, setSelected] = useState<SelectedProfile>(null);
  const queryClient = useQueryClient();

  const machineListQuery =
    useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint();
  const filamentListQuery =
    useOpenFarmApiEndpointsProfilesListFilamentProfilesEndpoint();
  const processListQuery =
    useOpenFarmApiEndpointsProfilesListProcessProfilesEndpoint();

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
  const processDetailQuery =
    useOpenFarmApiEndpointsProfilesGetProcessProfileEndpoint(
      selected?.type === 'Process' ? selected.id : '',
      { query: { enabled: selected?.type === 'Process' } },
    );

  const machineProfiles =
    (machineListQuery.data?.data as ListMachineProfilesResponse | undefined)
      ?.profiles ?? [];
  const filamentProfiles =
    (filamentListQuery.data?.data as ListFilamentProfilesResponse | undefined)
      ?.profiles ?? [];
  const processProfiles =
    (processListQuery.data?.data as ListProcessProfilesResponse | undefined)
      ?.profiles ?? [];

  const detailQuery =
    selected?.type === 'Machine'
      ? machineDetailQuery
      : selected?.type === 'Filament'
        ? filamentDetailQuery
        : processDetailQuery;
  const detail = detailQuery.data?.data as
    | MachineProfileDetailResponse
    | FilamentProfileDetailResponse
    | ProcessProfileDetailResponse
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
        description="Machine, process, and filament profiles imported from slicer bundles"
        action={<ImportProfilesDialog />}
      />
      <Tabs defaultValue="machine">
        <TabsList>
          <TabsTrigger value="machine">Machine</TabsTrigger>
          <TabsTrigger value="process">Process</TabsTrigger>
          <TabsTrigger value="filament">Filament</TabsTrigger>
        </TabsList>
        <TabsContent value="machine">
          {machineListQuery.isLoading ? (
            <Skeleton className="h-64 w-full" />
          ) : machineProfiles.length === 0 ? (
            <EmptyState
              icon={<SettingsIcon className="size-8 text-muted-foreground" />}
              title="No machine profiles yet"
              description="Import a slicer bundle to populate this catalog."
              action={<ImportProfilesDialog />}
            />
          ) : (
            <MachineProfilesTable
              profiles={machineProfiles}
              onRowClick={(id) => setSelected({ type: 'Machine', id })}
            />
          )}
        </TabsContent>
        <TabsContent value="process">
          {processListQuery.isLoading ? (
            <Skeleton className="h-64 w-full" />
          ) : processProfiles.length === 0 ? (
            <EmptyState
              icon={<LayersIcon className="size-8 text-muted-foreground" />}
              title="No process profiles yet"
              description="Import a slicer bundle to populate this catalog."
              action={<ImportProfilesDialog />}
            />
          ) : (
            <ProcessProfilesTable
              profiles={processProfiles}
              onRowClick={(id) => setSelected({ type: 'Process', id })}
            />
          )}
        </TabsContent>
        <TabsContent value="filament">
          {filamentListQuery.isLoading ? (
            <Skeleton className="h-64 w-full" />
          ) : filamentProfiles.length === 0 ? (
            <EmptyState
              icon={<SwatchBookIcon className="size-8 text-muted-foreground" />}
              title="No filament profiles yet"
              description="Import a slicer bundle to populate this catalog."
              action={<ImportProfilesDialog />}
            />
          ) : (
            <FilamentProfilesTable
              profiles={filamentProfiles}
              onRowClick={(id) => setSelected({ type: 'Filament', id })}
            />
          )}
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
        // Process profiles have no update endpoint yet — view-only (no onSave renders no Save button).
        onSave={selected?.type === 'Process' ? undefined : handleSaveRawData}
        isSaving={
          updateMachineMutation.isPending || updateFilamentMutation.isPending
        }
      />
    </div>
  );
}
