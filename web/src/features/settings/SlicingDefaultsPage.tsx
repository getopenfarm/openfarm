import { PackageIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { toast } from 'sonner';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PageHeader } from '@/components/page-header/PageHeader';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import type { AvailableSlicerResponse } from '@/lib/api/generated/endpoints.schemas';
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
          toast.success('Default slicer updated.');
          query.refetch();
        },
        onError: () => {
          toast.error('Failed to update the default slicer.');
        },
      },
    });

  function handleSave() {
    const [engine, version] = selection.split('::');
    if (!engine || !version) {
      return;
    }

    updateMutation.mutate({ data: { engine, version } });
  }

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="Slicing defaults"
        description="Choose which bundled slicer engine and version parts are sliced with by default."
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
        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="slicing-default-select">Default slicer</Label>
            <Select value={selection} onValueChange={setSelection}>
              <SelectTrigger id="slicing-default-select" className="w-full">
                <SelectValue placeholder="Choose a slicer version" />
              </SelectTrigger>
              <SelectContent>
                {available.map((slicer) => {
                  const key = selectionKey(slicer);
                  return (
                    <SelectItem key={key} value={key}>
                      {slicer.engine} {slicer.version} ({slicer.platform})
                    </SelectItem>
                  );
                })}
              </SelectContent>
            </Select>
            {effectiveDefault && !effectiveDefault.isExplicit && (
              <p className="text-muted-foreground text-xs">
                No explicit default is set — auto-selected the newest available
                version.
              </p>
            )}
          </div>
          <div>
            <Button
              onClick={handleSave}
              disabled={updateMutation.isPending || !selection}
            >
              {updateMutation.isPending ? 'Saving…' : 'Save default'}
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
