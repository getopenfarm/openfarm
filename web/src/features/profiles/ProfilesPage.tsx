import { useQueryClient } from '@tanstack/react-query';
import { SettingsIcon, SwatchBookIcon } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
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
import { MachineProfilesTable } from './components/MachineProfilesTable';
import { ProfileRawDataDialog } from './components/ProfileRawDataDialog';

type SelectedProfile = {
  type: 'Machine' | 'Filament';
  id: string;
} | null;

// User-imported profiles only (FR-013) — process profiles are exclusively System-sourced after 009
// and never appear on this browse/import page; select/narrow them from the printer page and part
// detail page instead.
export function ProfilesPage() {
  const navigate = useNavigate();
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
      ?.items ?? [];
  const filamentProfiles =
    (filamentListQuery.data?.data as ListFilamentProfilesResponse | undefined)
      ?.items ?? [];

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
        action={
          <Button onClick={() => navigate('/profiles/import')}>Import</Button>
        }
      />
      <Tabs defaultValue="machine">
        <TabsList>
          <TabsTrigger value="machine">Machine</TabsTrigger>
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
              action={
                <Button onClick={() => navigate('/profiles/import')}>
                  Import
                </Button>
              }
            />
          ) : (
            <MachineProfilesTable
              profiles={machineProfiles}
              onRowClick={(id) => setSelected({ type: 'Machine', id })}
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
              action={
                <Button onClick={() => navigate('/profiles/import')}>
                  Import
                </Button>
              }
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
        onSave={handleSaveRawData}
        isSaving={
          updateMachineMutation.isPending || updateFilamentMutation.isPending
        }
      />
    </div>
  );
}
