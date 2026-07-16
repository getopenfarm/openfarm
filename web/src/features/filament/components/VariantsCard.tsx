import { PencilIcon, PlusIcon, Trash2Icon } from 'lucide-react';
import { useState } from 'react';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import type {
  FilamentProfileResponse,
  ListFilamentProfilesResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsProfilesListFilamentProfilesEndpoint } from '@/lib/api/generated/profiles/profiles';
import type { FilamentVariant } from '../data/mockFilamentStore';
import { formatWeightGrams } from '../lib/weightLabel';
import { VariantDialog } from './VariantDialog';

function overrideSummary(
  variant: FilamentVariant,
  allProfiles: FilamentProfileResponse[],
): string {
  const overrides = variant.profileOverrides;
  if (!overrides || Object.keys(overrides).length === 0) {
    return 'Uses default profile';
  }
  const names = Object.entries(overrides)
    .filter((entry): entry is [string, string] => entry[1] !== undefined)
    .map(([nozzleSize, profileId]) => {
      const profile = allProfiles.find((p) => p.id === profileId);
      return `${nozzleSize}mm ${profile?.name ?? 'unknown profile'}`;
    });
  return `Override: ${names.join(', ')}`;
}

interface VariantsCardProps {
  variants: FilamentVariant[];
  onAdd: (variant: FilamentVariant) => void;
  onEdit: (variant: FilamentVariant) => void;
  onRemove: (variantId: string) => void;
}

export function VariantsCard({
  variants,
  onAdd,
  onEdit,
  onRemove,
}: VariantsCardProps) {
  const [dialogState, setDialogState] = useState<
    { mode: 'add' } | { mode: 'edit'; variant: FilamentVariant } | null
  >(null);
  const filamentProfilesQuery =
    useOpenFarmApiEndpointsProfilesListFilamentProfilesEndpoint();
  const allProfiles =
    (
      filamentProfilesQuery.data?.data as
        | ListFilamentProfilesResponse
        | undefined
    )?.profiles ?? [];

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between">
        <CardTitle>Variants ({variants.length})</CardTitle>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => setDialogState({ mode: 'add' })}
        >
          <PlusIcon /> Add variant
        </Button>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        {variants.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            No variants yet
          </p>
        ) : (
          variants.map((variant) => (
            <div
              key={variant.id}
              className="flex items-center gap-3 rounded-lg border px-3 py-2"
            >
              <Swatch colorHex={variant.colorHex} size="md" />
              <div className="flex flex-1 flex-col gap-0.5">
                <span className="text-sm font-medium">{variant.name}</span>
                <span className="font-mono text-xs text-muted-foreground">
                  {variant.weightsGrams.map(formatWeightGrams).join(' · ')}
                </span>
                <span className="text-xs text-muted-foreground">
                  {overrideSummary(variant, allProfiles)}
                </span>
              </div>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                aria-label={`Edit ${variant.name}`}
                onClick={() => setDialogState({ mode: 'edit', variant })}
              >
                <PencilIcon />
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                aria-label={`Remove ${variant.name}`}
                onClick={() => onRemove(variant.id)}
              >
                <Trash2Icon className="text-destructive" />
              </Button>
            </div>
          ))
        )}
      </CardContent>

      {dialogState && (
        <VariantDialog
          mode={dialogState.mode}
          open={dialogState !== null}
          onOpenChange={(open) => {
            if (!open) {
              setDialogState(null);
            }
          }}
          initialVariant={
            dialogState.mode === 'edit' ? dialogState.variant : undefined
          }
          onSave={(variant) => {
            if (dialogState.mode === 'add') {
              onAdd(variant);
            } else {
              onEdit(variant);
            }
            setDialogState(null);
          }}
        />
      )}
    </Card>
  );
}
