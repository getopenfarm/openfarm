import { useQueryClient } from '@tanstack/react-query';
import { PackageIcon } from 'lucide-react';
import { type FormEvent, useEffect, useState } from 'react';
import { toast } from 'sonner';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PermissionButton as Button } from '@/components/PermissionButton';
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
import { formatSlicer } from '@/features/slicing/formatSlicer';
import { invalidateSlicingChecks } from '@/features/slicing/invalidateSlicingChecks';
import { SlicerReadinessPanel } from '@/features/slicing/SlicerReadinessPanel';
import type { AvailableSlicerResponse } from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsSettingsGetSlicingDefaultsEndpoint,
  useOpenFarmApiEndpointsSettingsUpdateSlicingDefaultsEndpoint,
} from '@/lib/api/generated/settings/settings';
import { SettingsRow, SettingsRows, SettingsSection } from './SettingsParts';

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

    updateMutation.mutate({
      data: {
        engine,
        version,
      },
    });
  }

  return (
    <SettingsSection
      title="Slicing defaults"
      description="Choose the default slicer engine and version."
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
              <Select value={selection} onValueChange={setSelection}>
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

          <div className="mt-4 flex items-center gap-2">
            <Button
              permission="settings:slicing-defaults:update"
              type="submit"
              form={FORM_ID}
              disabled={updateMutation.isPending || !selection}
            >
              {updateMutation.isPending && <Spinner data-icon="inline-start" />}
              {updateMutation.isPending ? 'Saving…' : 'Save'}
            </Button>
          </div>
          {/* Empty form: a form ancestor makes Radix Select render a native select. */}
          <form id={FORM_ID} onSubmit={handleSave} />
        </>
      )}
    </SettingsSection>
  );
}
