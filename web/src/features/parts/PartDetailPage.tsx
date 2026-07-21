import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  getOpenFarmApiEndpointsPartsListPartsEndpointQueryKey,
  useOpenFarmApiEndpointsPartsDeletePartEndpoint,
  useOpenFarmApiEndpointsPartsGetPartEndpoint,
} from '@/lib/api/generated/parts/parts';
import { AddOrReplacePartDialog } from './components/AddOrReplacePartDialog';
import {
  type DisplayFilamentRequirement,
  PartFilamentRequirementsList,
} from './components/PartFilamentRequirementsList';
import { PartThumbnailGallery } from './components/PartThumbnailGallery';
import { usePartsHub } from './hooks/usePartsHub';

export function PartDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  usePartsHub();

  const [replaceOpen, setReplaceOpen] = useState(false);

  const partQuery = useOpenFarmApiEndpointsPartsGetPartEndpoint(id ?? '');
  const part = partQuery.data?.data;

  const deleteMutation = useOpenFarmApiEndpointsPartsDeletePartEndpoint({
    mutation: {
      onSuccess: () => {
        toast.success('Part deleted.');
        queryClient.invalidateQueries({
          queryKey: getOpenFarmApiEndpointsPartsListPartsEndpointQueryKey(),
        });
        navigate('/parts');
      },
      onError: () => toast.error('Could not delete this part.'),
    },
  });

  function handleDelete() {
    if (!part?.id) return;
    const confirmed = window.confirm(
      `Delete ${part.name}? This can't be undone.`,
    );
    if (confirmed) {
      deleteMutation.mutate({ id: part.id });
    }
  }

  const requirements: DisplayFilamentRequirement[] =
    part?.filamentRequirements?.map((requirement) => ({
      key: requirement.id ?? '',
      materialType: requirement.materialType,
      detectedColorHex: requirement.detectedColorHex,
      detectedSubBrand: requirement.detectedSubBrand,
      narrowFilamentVariantId: requirement.narrowFilamentVariantId,
      narrowFilamentVariantUnresolved:
        requirement.narrowFilamentVariantUnresolved,
    })) ?? [];

  return (
    <div className="flex flex-col gap-6">
      <BackLink to="/parts">Back to parts</BackLink>

      {partQuery.isLoading ? (
        <Skeleton className="h-64 w-full" />
      ) : !part ? (
        <p className="text-sm text-muted-foreground">Part not found.</p>
      ) : (
        <>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <h2 className="font-heading text-2xl font-semibold">
                {part.name}
              </h2>
              <p className="font-mono text-sm text-muted-foreground">
                {part.fileFormat} · {part.nozzleDiameterMm} mm nozzle
                {part.requiresSlicing ? ' · requires slicing' : ' · pre-sliced'}
              </p>
              <div className="mt-2 flex flex-wrap gap-1">
                {(part.tags ?? []).map((tag) => (
                  <Badge key={tag} variant="outline" className="font-normal">
                    {tag}
                  </Badge>
                ))}
              </div>
            </div>
            <div className="flex items-center gap-2">
              <Button variant="outline" onClick={() => setReplaceOpen(true)}>
                Replace file
              </Button>
              <Button
                variant="outline"
                onClick={handleDelete}
                disabled={deleteMutation.isPending}
              >
                Delete
              </Button>
            </div>
          </div>

          <div className="grid grid-cols-1 gap-4 lg:grid-cols-[1.2fr_1fr]">
            <div className="flex flex-col gap-4">
              <div>
                <p className="mb-1.5 text-sm text-muted-foreground">
                  Thumbnails
                </p>
                <PartThumbnailGallery
                  thumbnails={part.thumbnails}
                  plateCount={part.plateCount}
                  tileSize="8rem"
                />
              </div>
              <div>
                <p className="mb-1.5 text-sm text-muted-foreground">Estimate</p>
                {part.estimateStatus === 'Available' ? (
                  <p className="text-sm">
                    {part.estimatedPrintTimeMinutes} min ·{' '}
                    {part.estimatedFilamentWeightGrams} g
                  </p>
                ) : (
                  <p className="text-sm text-muted-foreground">
                    {part.estimateStatus === 'Unavailable'
                      ? 'Estimate unavailable — the deferred slice failed.'
                      : 'Estimate still resolving…'}
                  </p>
                )}
              </div>
            </div>
            <div>
              <p className="mb-1.5 text-sm text-muted-foreground">
                Filament requirements
              </p>
              <PartFilamentRequirementsList requirements={requirements} />
            </div>
          </div>

          <AddOrReplacePartDialog
            mode="replace"
            partId={part.id}
            partName={part.name}
            open={replaceOpen}
            onOpenChange={setReplaceOpen}
          />
        </>
      )}
    </div>
  );
}
