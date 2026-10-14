import { useQueryClient } from '@tanstack/react-query';
import { PackageIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { FormFooter } from '@/components/form-footer/FormFooter';
import { Page } from '@/components/page/Page';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import {
  Field,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from '@/components/ui/field';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import { Spinner } from '@/components/ui/spinner';
import { ProfilePickerCombobox } from '@/features/profiles/components/ProfilePickerCombobox';
import type {
  AvailableSlicerResponse,
  EstimationProfilesRequest,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsProfilesGetMachineProfileEndpoint } from '@/lib/api/generated/profiles/profiles';
import {
  useOpenFarmApiEndpointsSettingsGetSlicingDefaultsEndpoint,
  useOpenFarmApiEndpointsSettingsUpdateSlicingDefaultsEndpoint,
} from '@/lib/api/generated/settings/settings';

function selectionKey(
  slicer: Pick<AvailableSlicerResponse, 'engine' | 'version'>,
) {
  return `${slicer.engine}::${slicer.version}`;
}

export function SlicingDefaultsPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const query = useOpenFarmApiEndpointsSettingsGetSlicingDefaultsEndpoint();
  const data = query.data?.data;
  const available = data?.available ?? [];
  const effectiveDefault = data?.default;

  const [selection, setSelection] = useState('');
  const [profiles, setProfiles] =
    useState<Partial<EstimationProfilesRequest> | null>(null);
  const [machineName, setMachineName] = useState<string | null>(null);
  const [profilesDirty, setProfilesDirty] = useState(false);
  useEffect(() => {
    setProfiles(data?.estimationProfiles ?? null);
    setProfilesDirty(false);
  }, [data?.estimationProfiles]);
  const machineQuery = useOpenFarmApiEndpointsProfilesGetMachineProfileEndpoint(
    profiles?.machineProfileId ?? '',
    {
      query: { enabled: Boolean(profiles?.machineProfileId) && !machineName },
    },
  );
  const effectiveMachineName = machineName ?? machineQuery.data?.data?.name;
  const incompleteProfiles =
    profiles !== null &&
    (!profiles.machineProfileId ||
      !profiles.processProfileId ||
      !profiles.filamentProfileId ||
      !profiles.buildPlateType);

  // Follows the effective default whenever it (re)loads — an admin who hasn't touched the picker
  // yet always sees the currently-effective slicer selected, explicit or auto-defaulted alike.
  // biome-ignore lint/correctness/useExhaustiveDependencies: only re-syncs when the effective default's own identity changes, not on every query refetch.
  useEffect(() => {
    setSelection(effectiveDefault ? selectionKey(effectiveDefault) : '');
  }, [effectiveDefault?.engine, effectiveDefault?.version]);

  const updateMutation =
    useOpenFarmApiEndpointsSettingsUpdateSlicingDefaultsEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Slicing defaults updated.');
          query.refetch();
          queryClient.invalidateQueries({
            predicate: ({ queryKey }) =>
              typeof queryKey[0] === 'string' &&
              (queryKey[0].startsWith('/api/v1/profiles') ||
                queryKey[0].startsWith('/api/v1/filaments') ||
                queryKey[0].startsWith('/api/v1/print-jobs')),
          });
        },
        onError: async (error: unknown) => {
          let message =
            'Failed to update slicing defaults. Check profile compatibility with the selected engine and machine.';
          if (error instanceof Response) {
            const problem = await error
              .clone()
              .json()
              .catch(() => null);
            if (typeof problem?.detail === 'string') message = problem.detail;
          }
          toast.error(message);
        },
      },
    });

  function handleSave() {
    const [engine, version] = selection.split('::');
    if (!engine || !version) {
      return;
    }

    if (incompleteProfiles) return;
    updateMutation.mutate({
      data: {
        engine,
        version,
        ...(profilesDirty
          ? { estimationProfiles: profiles as EstimationProfilesRequest | null }
          : {}),
      },
    });
  }

  return (
    <Page className="flex-1">
      <PageHeader
        above={
          <BackLink to="/settings" className="mb-1">
            Back to settings
          </BackLink>
        }
        title="Slicing defaults"
        description="Choose the default slicer and profiles for library estimates. STL and geometry-only 3MF use these profiles; project 3MF uses its embedded settings."
      />

      {query.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : query.isError ? (
        <ErrorState
          description="Couldn't load the bundled slicer catalog."
          onRetry={() => query.refetch()}
        />
      ) : available.length === 0 ? (
        <EmptyState
          icon={<PackageIcon className="size-8 text-muted-foreground" />}
          title="No slicer available"
          description="No slicer engine/version is bundled with this install yet. Provision one via tools/provision-slicers."
        />
      ) : (
        <div className="flex flex-1 flex-col gap-6">
          <Card>
            <CardContent>
              <FieldGroup className="gap-4">
                <Field className="flex flex-col gap-1.5">
                  <FieldLabel htmlFor="slicing-default-select">
                    Default slicer
                  </FieldLabel>
                  <Select
                    value={selection}
                    onValueChange={(next) => {
                      if (next.split('::')[0] !== selection.split('::')[0]) {
                        setProfiles(null);
                        setMachineName(null);
                        setProfilesDirty(true);
                      }
                      setSelection(next);
                    }}
                  >
                    <SelectTrigger
                      id="slicing-default-select"
                      className="w-full"
                    >
                      <SelectValue placeholder="Choose a slicer version" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        {available.map((slicer) => {
                          const key = selectionKey(slicer);
                          return (
                            <SelectItem key={key} value={key}>
                              {slicer.engine} {slicer.version} (
                              {slicer.platform})
                            </SelectItem>
                          );
                        })}
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                  {effectiveDefault && !effectiveDefault.isExplicit && (
                    <p className="text-muted-foreground text-xs">
                      No explicit default is set — auto-selected the newest
                      available version.
                    </p>
                  )}
                </Field>
                <FieldSet className="rounded-md border p-4">
                  <FieldLegend>Default estimation profiles</FieldLegend>
                  <p className="text-sm text-muted-foreground">
                    Optional. Select all four settings to estimate STL and
                    geometry-only 3MF without registering a printer. The
                    filament choice is for estimation and does not restrict
                    production material.
                  </p>
                  {!profiles && (
                    <p className="text-sm text-muted-foreground">
                      No estimation defaults configured.
                    </p>
                  )}
                  <Field>
                    <FieldLabel htmlFor="estimation-machine">
                      Machine profile
                    </FieldLabel>
                    <ProfilePickerCombobox
                      kind="machine"
                      id="estimation-machine"
                      ariaLabel="Estimation machine profile"
                      value={profiles?.machineProfileId}
                      onChange={(id, name) => {
                        setProfiles(id ? { machineProfileId: id } : null);
                        setMachineName(name ?? null);
                        setProfilesDirty(true);
                      }}
                    />
                  </Field>
                  <Field data-disabled={!profiles?.machineProfileId}>
                    <FieldLabel htmlFor="estimation-process">
                      Process profile
                    </FieldLabel>
                    <ProfilePickerCombobox
                      kind="process"
                      id="estimation-process"
                      ariaLabel="Estimation process profile"
                      value={profiles?.processProfileId}
                      disabled={!profiles?.machineProfileId}
                      compatibleWithMachineName={effectiveMachineName}
                      onChange={(id) => {
                        setProfiles((p) => ({
                          ...p,
                          processProfileId: id ?? undefined,
                        }));
                        setProfilesDirty(true);
                      }}
                    />
                  </Field>
                  <Field data-disabled={!profiles?.machineProfileId}>
                    <FieldLabel htmlFor="estimation-filament">
                      Filament profile
                    </FieldLabel>
                    <ProfilePickerCombobox
                      kind="filament"
                      id="estimation-filament"
                      ariaLabel="Estimation filament profile"
                      value={profiles?.filamentProfileId}
                      disabled={!profiles?.machineProfileId}
                      compatibleWithMachineName={effectiveMachineName}
                      onChange={(id) => {
                        setProfiles((p) => ({
                          ...p,
                          filamentProfileId: id ?? undefined,
                        }));
                        setProfilesDirty(true);
                      }}
                    />
                  </Field>
                  <Field data-disabled={!profiles?.machineProfileId}>
                    <FieldLabel htmlFor="estimation-plate">
                      Build plate
                    </FieldLabel>
                    <Select
                      value={profiles?.buildPlateType ?? ''}
                      disabled={!profiles?.machineProfileId}
                      onValueChange={(plate) => {
                        setProfiles((p) => ({ ...p, buildPlateType: plate }));
                        setProfilesDirty(true);
                      }}
                    >
                      <SelectTrigger id="estimation-plate">
                        <SelectValue placeholder="Select a build plate" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectGroup>
                          {[
                            ['Cool', 'Cool Plate'],
                            ['Engineering', 'Engineering Plate'],
                            ['HighTemp', 'High Temp Plate'],
                            ['TexturedPei', 'Textured PEI Plate'],
                            ['TexturedCool', 'Textured Cool Plate'],
                            ['Supertack', 'Supertack Plate'],
                          ].map(([id, label]) => (
                            <SelectItem key={id} value={id}>
                              {label}
                            </SelectItem>
                          ))}
                        </SelectGroup>
                      </SelectContent>
                    </Select>
                  </Field>
                  {profiles && (
                    <Button
                      type="button"
                      variant="outline"
                      onClick={() => {
                        setProfiles(null);
                        setMachineName(null);
                        setProfilesDirty(true);
                      }}
                    >
                      Clear estimation profiles
                    </Button>
                  )}
                  {incompleteProfiles && (
                    <p className="text-sm text-muted-foreground">
                      Select machine, process, filament, and build plate before
                      saving.
                    </p>
                  )}
                  <p className="text-sm text-muted-foreground">
                    Changes apply to new estimates and explicit Reprocess. Saved
                    parts are not automatically recalculated.
                  </p>
                </FieldSet>
              </FieldGroup>
            </CardContent>
          </Card>
          <FormFooter
            end={
              <>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => navigate('/settings')}
                >
                  Cancel
                </Button>
                <Button
                  type="button"
                  onClick={handleSave}
                  disabled={
                    updateMutation.isPending || !selection || incompleteProfiles
                  }
                >
                  {updateMutation.isPending && (
                    <Spinner data-icon="inline-start" />
                  )}
                  {updateMutation.isPending ? 'Saving…' : 'Save'}
                </Button>
              </>
            }
          />
        </div>
      )}
    </Page>
  );
}
