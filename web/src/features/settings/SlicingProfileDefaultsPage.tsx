import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { toast } from 'sonner';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import type {
  FilamentDefaultResponseItem,
  ListFilamentProfilesResponse,
  ListMachineProfilesResponse,
  SlicingProfileDefaultsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsProfilesListFilamentProfilesEndpoint,
  useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint,
} from '@/lib/api/generated/profiles/profiles';
import {
  useOpenFarmApiEndpointsSettingsGetSlicingProfileDefaultsEndpoint,
  useOpenFarmApiEndpointsSettingsUpdateDefaultMachineProfileEndpoint,
  useOpenFarmApiEndpointsSettingsUpdateOrganizationFilamentDefaultEndpoint,
} from '@/lib/api/generated/settings/settings';

export function SlicingProfileDefaultsPage() {
  const defaultsQuery =
    useOpenFarmApiEndpointsSettingsGetSlicingProfileDefaultsEndpoint();
  const machineListQuery =
    useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint();
  const filamentListQuery =
    useOpenFarmApiEndpointsProfilesListFilamentProfilesEndpoint();

  const defaults = defaultsQuery.data?.data as
    | SlicingProfileDefaultsResponse
    | undefined;
  const machineProfiles =
    (machineListQuery.data?.data as ListMachineProfilesResponse | undefined)
      ?.profiles ?? [];
  const filamentProfiles =
    (filamentListQuery.data?.data as ListFilamentProfilesResponse | undefined)
      ?.profiles ?? [];
  const filamentDefaults = defaults?.filamentDefaults ?? [];

  const [machineSelection, setMachineSelection] = useState('');
  // Follows the currently configured default whenever it (re)loads, same pattern as
  // SlicingDefaultsPage — an admin who hasn't touched the picker yet always sees what's actually set.
  // biome-ignore lint/correctness/useExhaustiveDependencies: only re-syncs when the default's own identity changes, not on every query refetch.
  useEffect(() => {
    setMachineSelection(defaults?.defaultMachineProfile?.id ?? '');
  }, [defaults?.defaultMachineProfile?.id]);

  const updateMachineMutation =
    useOpenFarmApiEndpointsSettingsUpdateDefaultMachineProfileEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Default machine profile updated.');
          defaultsQuery.refetch();
        },
        onError: () =>
          toast.error('Failed to update the default machine profile.'),
      },
    });

  function handleSaveMachineDefault() {
    if (!machineSelection) {
      return;
    }
    updateMachineMutation.mutate({
      data: { machineProfileId: machineSelection },
    });
  }

  const [materialType, setMaterialType] = useState('');
  const [filamentSelection, setFilamentSelection] = useState('');

  const updateFilamentDefaultMutation =
    useOpenFarmApiEndpointsSettingsUpdateOrganizationFilamentDefaultEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Filament default updated.');
          defaultsQuery.refetch();
          setMaterialType('');
          setFilamentSelection('');
        },
        onError: () => toast.error('Failed to update the filament default.'),
      },
    });

  function handleSaveFilamentDefault() {
    const trimmed = materialType.trim();
    if (!trimmed || !filamentSelection) {
      return;
    }
    updateFilamentDefaultMutation.mutate({
      materialType: trimmed,
      data: { filamentProfileId: filamentSelection },
    });
  }

  function handleEditFilamentDefault(entry: FilamentDefaultResponseItem) {
    setMaterialType(entry.materialType ?? '');
    setFilamentSelection(entry.filamentProfileId ?? '');
  }

  const isLoading =
    defaultsQuery.isLoading ||
    machineListQuery.isLoading ||
    filamentListQuery.isLoading;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Slicing Profile Defaults"
        description="Choose the default machine profile and per-material filament profiles used to slice."
      />

      {isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : defaultsQuery.isError ? (
        <ErrorState
          description="Couldn't load the current slicing profile defaults."
          onRetry={() => defaultsQuery.refetch()}
        />
      ) : (
        <div className="flex flex-col gap-6">
          <div className="flex flex-col gap-2">
            <h2 className="text-sm font-semibold">Default machine profile</h2>
            {machineProfiles.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                No machine profiles imported yet — import one on the{' '}
                <Link to="/profiles" className="underline">
                  Profiles
                </Link>{' '}
                page.
              </p>
            ) : (
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="default-machine-profile-select">
                  Machine profile
                </Label>
                <Select
                  value={machineSelection}
                  onValueChange={setMachineSelection}
                >
                  <SelectTrigger
                    id="default-machine-profile-select"
                    className="w-full"
                  >
                    <SelectValue placeholder="Choose a machine profile" />
                  </SelectTrigger>
                  <SelectContent>
                    {machineProfiles.map((profile) => (
                      <SelectItem key={profile.id} value={profile.id ?? ''}>
                        {profile.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {defaults?.defaultMachineProfile &&
                  (defaults.defaultMachineProfile.defaultProcessProfile ? (
                    <p className="text-xs text-muted-foreground">
                      Paired process profile:{' '}
                      {
                        defaults.defaultMachineProfile.defaultProcessProfile
                          .name
                      }
                    </p>
                  ) : (
                    <p className="text-xs text-muted-foreground">
                      No process profile paired yet — pair one on the{' '}
                      <Link to="/profiles" className="underline">
                        Profiles
                      </Link>{' '}
                      page.
                    </p>
                  ))}
                <div>
                  <Button
                    onClick={handleSaveMachineDefault}
                    disabled={
                      updateMachineMutation.isPending || !machineSelection
                    }
                  >
                    {updateMachineMutation.isPending
                      ? 'Saving…'
                      : 'Save default'}
                  </Button>
                </div>
              </div>
            )}
          </div>

          <div className="flex flex-col gap-2">
            <h2 className="text-sm font-semibold">Filament defaults</h2>
            {filamentProfiles.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                No filament profiles imported yet — import one on the{' '}
                <Link to="/profiles" className="underline">
                  Profiles
                </Link>{' '}
                page.
              </p>
            ) : (
              <>
                <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
                  <div className="flex flex-1 flex-col gap-1.5">
                    <Label htmlFor="filament-default-material-type">
                      Material type
                    </Label>
                    <Input
                      id="filament-default-material-type"
                      placeholder="e.g. PLA, PETG"
                      value={materialType}
                      onChange={(e) => setMaterialType(e.target.value)}
                    />
                  </div>
                  <div className="flex flex-1 flex-col gap-1.5">
                    <Label htmlFor="filament-default-profile-select">
                      Filament profile
                    </Label>
                    <Select
                      value={filamentSelection}
                      onValueChange={setFilamentSelection}
                    >
                      <SelectTrigger
                        id="filament-default-profile-select"
                        className="w-full"
                      >
                        <SelectValue placeholder="Choose a filament profile" />
                      </SelectTrigger>
                      <SelectContent>
                        {filamentProfiles.map((profile) => (
                          <SelectItem key={profile.id} value={profile.id ?? ''}>
                            {profile.name}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                  <Button
                    onClick={handleSaveFilamentDefault}
                    disabled={
                      updateFilamentDefaultMutation.isPending ||
                      !materialType.trim() ||
                      !filamentSelection
                    }
                  >
                    {updateFilamentDefaultMutation.isPending
                      ? 'Saving…'
                      : 'Save default'}
                  </Button>
                </div>

                {filamentDefaults.length > 0 && (
                  <ul className="mt-2 flex flex-col gap-1">
                    {filamentDefaults.map((entry) => (
                      <li key={entry.materialType}>
                        <button
                          type="button"
                          onClick={() => handleEditFilamentDefault(entry)}
                          className="flex w-full items-center justify-between rounded-md border px-3 py-2 text-left text-sm transition-colors hover:bg-accent"
                        >
                          <span className="font-medium">
                            {entry.materialType}
                          </span>
                          <span className="text-muted-foreground">
                            {entry.filamentProfileName}
                          </span>
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
