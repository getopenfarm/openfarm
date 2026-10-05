import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { toast } from 'sonner';
import { BackLink } from '@/components/back-link/BackLink';
import { ConfirmDialog } from '@/components/confirm-dialog/ConfirmDialog';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  getOpenFarmApiEndpointsPartsGetPartEndpointQueryKey,
  getOpenFarmApiEndpointsPartsListPartsEndpointQueryKey,
  useOpenFarmApiEndpointsPartsDeletePartEndpoint,
  useOpenFarmApiEndpointsPartsGetPartEndpoint,
  useOpenFarmApiEndpointsPartsUpdatePartFilamentRequirementNarrowingEndpoint,
} from '@/lib/api/generated/parts/parts';
import { AddOrReplacePartDialog } from './components/AddOrReplacePartDialog';
import { PartEstimate } from './components/PartEstimate';
import { PartEstimationSource } from './components/PartEstimationSource';
import {
  type DisplayFilamentRequirement,
  PartFilamentRequirementsList,
} from './components/PartFilamentRequirementsList';
import { PartThumbnailGallery } from './components/PartThumbnailGallery';
import { UnitsPerPrintForm } from './components/UnitsPerPrintForm';
import { usePartsHub } from './hooks/usePartsHub';

export function PartDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  usePartsHub();

  const [replaceOpen, setReplaceOpen] = useState(false);
  const [reprocessOpen, setReprocessOpen] = useState(false);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);

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

  function handleConfirmDelete() {
    if (!part?.id) return;
    deleteMutation.mutate({ id: part.id });
  }

  const narrowMutation =
    useOpenFarmApiEndpointsPartsUpdatePartFilamentRequirementNarrowingEndpoint({
      mutation: {
        onSuccess: () => {
          toast.success('Filament requirement updated.');
          queryClient.invalidateQueries({
            queryKey: getOpenFarmApiEndpointsPartsGetPartEndpointQueryKey(
              part?.id ?? '',
            ),
          });
        },
        onError: () =>
          toast.error('Could not update the filament requirement.'),
      },
    });

  function handleNarrowToProfile(
    requirementId: string,
    filamentProfileId: string | null,
  ) {
    if (!part?.id) return;
    narrowMutation.mutate({
      id: part.id,
      requirementId,
      data: { narrowFilamentProfileId: filamentProfileId },
    });
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
      narrowFilamentProfileId: requirement.narrowFilamentProfileId,
      narrowFilamentProfileUnresolved:
        requirement.narrowFilamentProfileUnresolved,
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
          {part.id && (
            <UnitsPerPrintForm
              key={`${part.id}-${part.unitsPerPrint}`}
              partId={part.id}
              unitsPerPrint={part.unitsPerPrint ?? 1}
            />
          )}
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <h2 className="font-heading text-2xl font-semibold">
                {part.name}
              </h2>
              <p className="font-mono text-sm text-muted-foreground">
                {part.fileFormat} ·{' '}
                {part.nozzleDiameterMm != null
                  ? `${part.nozzleDiameterMm} mm nozzle`
                  : 'Unknown nozzle'}
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
              <Button variant="outline" onClick={() => setReprocessOpen(true)}>
                Reprocess
              </Button>
              <Button variant="outline" onClick={() => setReplaceOpen(true)}>
                Replace file
              </Button>
              <Button
                variant="outline"
                onClick={() => setConfirmDeleteOpen(true)}
                disabled={deleteMutation.isPending}
              >
                Delete
              </Button>
            </div>
          </div>

          {part.printRequirementsStatus === 'Incomplete' && (
            <p className="text-sm text-muted-foreground">
              Print requirements incomplete — reprocess after printer setup, or
              replace the file with complete metadata before production.
            </p>
          )}
          {(part.processingWarnings ?? []).length > 0 && (
            <div
              role="status"
              className="rounded-lg border p-3 text-sm text-muted-foreground"
            >
              {part.processingWarnings?.map((warning) => (
                <p key={warning}>{warning}</p>
              ))}
            </div>
          )}
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
                <PartEstimationSource estimation={part.estimation} />
                <PartEstimate
                  status={part.estimateStatus}
                  minutes={part.estimatedPrintTimeMinutes}
                  grams={part.estimatedFilamentWeightGrams}
                />
              </div>
            </div>
            <div>
              <p className="mb-1.5 text-sm text-muted-foreground">
                Filament requirements
              </p>
              <PartFilamentRequirementsList
                requirements={requirements}
                onNarrowToProfile={handleNarrowToProfile}
              />
            </div>
          </div>

          <AddOrReplacePartDialog
            partId={part.id}
            partName={part.name}
            open={replaceOpen}
            onOpenChange={setReplaceOpen}
          />

          <AddOrReplacePartDialog
            partId={part.id}
            partName={part.name}
            mode="reprocess"
            open={reprocessOpen}
            onOpenChange={setReprocessOpen}
          />

          <ConfirmDialog
            open={confirmDeleteOpen}
            onOpenChange={setConfirmDeleteOpen}
            title="Delete part?"
            description={`Delete ${part.name}? This can't be undone.`}
            confirmLabel="Delete"
            onConfirm={handleConfirmDelete}
          />
        </>
      )}
    </div>
  );
}
