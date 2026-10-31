import { SettingsIcon, SwatchBookIcon, UploadIcon } from 'lucide-react';
import { useNavigate } from 'react-router';
import {
  DataTableTabsList,
  DataTableToolbar,
} from '@/components/data-table/DataTableToolbar';
import {
  serverListQueryOptions,
  useRetainedListData,
  useServerListState,
  useServerPageRecovery,
} from '@/components/data-table/useServerListState';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsTrigger } from '@/components/ui/tabs';
import type {
  ListFilamentProfilesResponse,
  ListMachineProfilesResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsProfilesListFilamentProfilesEndpoint,
  useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint,
} from '@/lib/api/generated/profiles/profiles';
import { FilamentProfilesTable } from './components/FilamentProfilesTable';
import { MachineProfilesTable } from './components/MachineProfilesTable';

// User-imported profiles only (FR-013) — process profiles are exclusively System-sourced after 009
// and never appear on this browse/import page; select/narrow them from the printer page and part
// detail page instead.
export function ProfilesPage() {
  const navigate = useNavigate();
  const machineList = useServerListState('name', 'name', true);
  const {
    page: machinePage,
    pageSize: machinePageSize,
    setPage: setMachinePage,
    setPageSize: setMachinePageSize,
  } = machineList;
  const filamentList = useServerListState('name', 'name', true);
  const {
    page: filamentPage,
    pageSize: filamentPageSize,
    setPage: setFilamentPage,
    setPageSize: setFilamentPageSize,
  } = filamentList;

  const machineListQuery =
    useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint(
      {
        ...machineList.params,
      },
      { query: serverListQueryOptions },
    );
  const filamentListQuery =
    useOpenFarmApiEndpointsProfilesListFilamentProfilesEndpoint(
      {
        ...filamentList.params,
      },
      { query: serverListQueryOptions },
    );

  const machineData = useRetainedListData(
    machineListQuery.data?.data as ListMachineProfilesResponse | undefined,
  );
  const machineProfiles = machineData?.items ?? [];
  const filamentData = useRetainedListData(
    filamentListQuery.data?.data as ListFilamentProfilesResponse | undefined,
  );
  const filamentProfiles = filamentData?.items ?? [];

  useServerPageRecovery(
    machinePage,
    machinePageSize,
    machineListQuery.data?.status === 200
      ? (machineListQuery.data.data.totalCount ?? 0)
      : 0,
    machineListQuery.isSuccess &&
      !machineListQuery.isFetching &&
      !machineListQuery.isPlaceholderData,
    setMachinePage,
  );

  useServerPageRecovery(
    filamentPage,
    filamentPageSize,
    filamentListQuery.data?.status === 200
      ? (filamentListQuery.data.data.totalCount ?? 0)
      : 0,
    filamentListQuery.isSuccess &&
      !filamentListQuery.isFetching &&
      !filamentListQuery.isPlaceholderData,
    setFilamentPage,
  );

  return (
    <Page>
      <PageHeader
        title="Profiles"
        description="Imported from slicer bundles."
        action={
          <Button
            permission="profiles:preview-import"
            onClick={() => navigate('/profiles/import')}
          >
            <UploadIcon data-icon="inline-start" />
            Import profiles
          </Button>
        }
      />
      <Tabs defaultValue="machine" className="gap-4">
        <DataTableToolbar>
          <DataTableTabsList aria-label="Profile type">
            <TabsTrigger value="machine">Machine</TabsTrigger>
            <TabsTrigger value="filament">Filament</TabsTrigger>
          </DataTableTabsList>
        </DataTableToolbar>
        <TabsContent value="machine" className="flex flex-col gap-3">
          {machineListQuery.isError && machineData ? (
            <ErrorState
              title="Could not refresh profiles"
              onRetry={() => machineListQuery.refetch()}
            />
          ) : null}
          {machineListQuery.isLoading ? (
            <Skeleton className="h-64 w-full" />
          ) : machineListQuery.isError && !machineData ? (
            <ErrorState
              title="Could not load machine profiles"
              onRetry={() => machineListQuery.refetch()}
            />
          ) : machineProfiles.length === 0 && machinePage === 1 ? (
            <EmptyState
              icon={<SettingsIcon className="size-8 text-muted-foreground" />}
              title="No machine profiles"
              description="Import a slicer bundle."
              action={
                <Button
                  permission="profiles:preview-import"
                  onClick={() => navigate('/profiles/import')}
                >
                  Import profiles
                </Button>
              }
            />
          ) : (
            <MachineProfilesTable
              sorting={{
                ...machineList.serverSorting,
                disabled: machineListQuery.isError,
                pending: machineListQuery.isFetching,
              }}
              pagination={{
                page: machinePage,
                pageSize: machinePageSize,
                totalCount: machineData?.totalCount ?? machineProfiles.length,
                onPageChange: setMachinePage,
                onPageSizeChange: (size) => {
                  setMachinePageSize(size);
                  setMachinePage(1);
                },
                disabled: machineListQuery.isError,
                pending:
                  machineListQuery.isFetching ||
                  machineListQuery.isPlaceholderData,
              }}
              profiles={machineProfiles}
              onRowClick={(id) => navigate(`/profiles/machine/${id}`)}
            />
          )}
        </TabsContent>
        <TabsContent value="filament" className="flex flex-col gap-3">
          {filamentListQuery.isError && filamentData ? (
            <ErrorState
              title="Could not refresh profiles"
              onRetry={() => filamentListQuery.refetch()}
            />
          ) : null}
          {filamentListQuery.isLoading ? (
            <Skeleton className="h-64 w-full" />
          ) : filamentListQuery.isError && !filamentData ? (
            <ErrorState
              title="Could not load filament profiles"
              onRetry={() => filamentListQuery.refetch()}
            />
          ) : filamentProfiles.length === 0 && filamentPage === 1 ? (
            <EmptyState
              icon={<SwatchBookIcon className="size-8 text-muted-foreground" />}
              title="No filament profiles"
              description="Import a slicer bundle."
              action={
                <Button
                  permission="profiles:preview-import"
                  onClick={() => navigate('/profiles/import')}
                >
                  Import profiles
                </Button>
              }
            />
          ) : (
            <FilamentProfilesTable
              sorting={{
                ...filamentList.serverSorting,
                disabled: filamentListQuery.isError,
                pending: filamentListQuery.isFetching,
              }}
              pagination={{
                page: filamentPage,
                pageSize: filamentPageSize,
                totalCount: filamentData?.totalCount ?? filamentProfiles.length,
                onPageChange: setFilamentPage,
                onPageSizeChange: (size) => {
                  setFilamentPageSize(size);
                  setFilamentPage(1);
                },
                disabled: filamentListQuery.isError,
                pending:
                  filamentListQuery.isFetching ||
                  filamentListQuery.isPlaceholderData,
              }}
              profiles={filamentProfiles}
              onRowClick={(id) => navigate(`/profiles/filament/${id}`)}
            />
          )}
        </TabsContent>
      </Tabs>
    </Page>
  );
}
