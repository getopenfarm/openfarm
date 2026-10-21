import { useQueryClient } from '@tanstack/react-query';
import { PackageIcon } from 'lucide-react';
import { type FormEvent, useEffect, useState } from 'react';
import { toast } from 'sonner';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Button } from '@/components/ui/button';
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
import { formatSlicer } from '@/features/slicing/formatSlicer';
import { invalidateSlicingChecks } from '@/features/slicing/invalidateSlicingChecks';
import { SlicerReadinessPanel } from '@/features/slicing/SlicerReadinessPanel';
import type {
  AvailableSlicerResponse,
  EstimationProfilesRequest,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsProfilesGetMachineProfileEndpoint } from '@/lib/api/generated/profiles/profiles';
import {
  useOpenFarmApiEndpointsSettingsGetSlicingDefaultsEndpoint,
  useOpenFarmApiEndpointsSettingsUpdateSlicingDefaultsEndpoint,
} from '@/lib/api/generated/settings/settings';
import {
  SettingsHint,
  SettingsRow,
  SettingsRows,
  SettingsSection,
  SettingsSubhead,
} from './SettingsParts';

const FORM_ID = 'slicing-defaults-form';

function selectionKey(
  slicer: Pick<AvailableSlicerResponse, 'engine' | 'version'>,
) {
  return `${slicer.engine}::${slicer.version}`;
}

export function SlicingDefaultsPage() {
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
          toast.success('Slicing defaults saved.');
          query.refetch();
          invalidateSlicingChecks(queryClient);
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
            'Could not save slicing defaults. Check profile compatibility with the engine and machine.';
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

  function handleSave(event: FormEvent) {
    event.preventDefault();
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
    <SettingsSection
      title="Slicing defaults"
      description="Defaults for estimates. Project 3MF uses its embedded settings."
    >
      {query.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : query.isError ? (
        <ErrorState
          description="Could not load slicers."
          onRetry={() => query.refetch()}
        />
      ) : available.length === 0 ? (
        <EmptyState
          icon={<PackageIcon className="size-8 text-muted-foreground" />}
          title="No slicer available"
          description="No slicer is bundled with this install."
        />
      ) : (
        <>
          <SettingsRows>
            <SettingsRow
              label="Default slicer"
              htmlFor="slicing-default-select"
              help={
                effectiveDefault && !effectiveDefault.isExplicit
                  ? 'No explicit default. The newest version is used. New parts only. Existing parts keep their slicer.'
                  : 'New parts only. Existing parts keep their slicer.'
              }
              wide
            >
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
                <SelectTrigger id="slicing-default-select" className="w-full">
                  <SelectValue placeholder="Slicer version" />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    {available.map((slicer) => {
                      const key = selectionKey(slicer);
                      return (
                        <SelectItem key={key} value={key}>
                          {formatSlicer(slicer.engine, slicer.version)} (
                          {slicer.platform})
                        </SelectItem>
                      );
                    })}
                  </SelectGroup>
                </SelectContent>
              </Select>
            </SettingsRow>
          </SettingsRows>
          {selection && (
            <SlicerReadinessPanel
              engine={selection.split('::')[0]}
              version={selection.split('::')[1]}
            />
          )}

          <SettingsSubhead>Estimation profiles</SettingsSubhead>
          <SettingsRows>
            <SettingsRow
              label="Machine profile"
              htmlFor="estimation-machine"
              help={profiles ? undefined : 'No estimation defaults.'}
              wide
            >
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
            </SettingsRow>
            <SettingsRow
              label="Process profile"
              htmlFor="estimation-process"
              wide
            >
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
            </SettingsRow>
            <SettingsRow
              label="Filament profile"
              htmlFor="estimation-filament"
              help="Does not restrict production material."
              wide
            >
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
            </SettingsRow>
            <SettingsRow label="Build plate" htmlFor="estimation-plate" wide>
              <Select
                value={profiles?.buildPlateType ?? ''}
                disabled={!profiles?.machineProfileId}
                onValueChange={(plate) => {
                  setProfiles((p) => ({ ...p, buildPlateType: plate }));
                  setProfilesDirty(true);
                }}
              >
                <SelectTrigger id="estimation-plate" className="w-full">
                  <SelectValue placeholder="Build plate" />
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
            </SettingsRow>
          </SettingsRows>
          <SettingsHint>
            Optional. Set all four to estimate STL and geometry-only 3MF without
            a printer. Changes apply to new estimates and Reprocess. Saved parts
            are not recalculated.
          </SettingsHint>
          {incompleteProfiles && (
            <SettingsHint>
              Set machine, process, filament, and build plate to save.
            </SettingsHint>
          )}

          <div className="mt-4 flex items-center gap-2">
            <Button
              type="submit"
              form={FORM_ID}
              disabled={
                updateMutation.isPending || !selection || incompleteProfiles
              }
            >
              {updateMutation.isPending && <Spinner data-icon="inline-start" />}
              {updateMutation.isPending ? 'Saving…' : 'Save'}
            </Button>
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
          </div>
          {/* Empty form: a form ancestor makes Radix Select render a native select. */}
          <form id={FORM_ID} onSubmit={handleSave} />
        </>
      )}
    </SettingsSection>
  );
}
