import { ArrowRightIcon } from 'lucide-react';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ProfilePickerCombobox } from '@/features/profiles/components/ProfilePickerCombobox';
import type {
  FilamentResponse,
  ListFilamentsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint } from '@/lib/api/generated/filaments/filaments';
import { formatGrams } from '@/lib/formatUnits';

export interface DisplayFilamentRequirement {
  /** Stable key — `id` for a saved Part's requirement, `requirementIndex` for a still-in-review
   * job preview (research.md §3a/§7). */
  key: string | number;
  materialType: string | null | undefined;
  detectedColorHex: string | null | undefined;
  detectedSubBrand: string | null | undefined;
  narrowFilamentVariantId?: string | null;
  narrowFilamentVariantUnresolved?: boolean;
  /** Only meaningful together with onNarrowToProfile (PartDetailPage, US4). */
  narrowFilamentProfileId?: string | null;
  narrowFilamentProfileUnresolved?: boolean;
  /** Per plate set, purge included. */
  estimatedGrams?: number | null;
}

interface PartFilamentRequirementsListProps {
  requirements: DisplayFilamentRequirement[];
  /** Shared catalog color-variant selector for preview, saved parts and queued jobs. */
  onNarrow?: (key: string | number, filamentVariantId: string | null) => void;
  /** Allows changing or clearing an existing legacy direct profile constraint. */
  onNarrowToProfile?: (
    requirementId: string,
    filamentProfileId: string | null,
  ) => void;
  /** `grid` lays requirements out two-up on wide screens. */
  layout?: 'list' | 'grid';
}

const NO_NARROW_VALUE = '__any__';

function requirementLabel(requirement: DisplayFilamentRequirement): string {
  if (!requirement.materialType) {
    return 'Any material';
  }
  return requirement.detectedSubBrand
    ? `${requirement.materialType} — ${requirement.detectedSubBrand}`
    : requirement.materialType;
}

/** Renders a Part's (or a still-in-review job preview's) one-or-more grouped filament
 * requirements — one badge per distinct (material, color) pair, each independently
 * narrowable to a specific Filament color-variant when `onNarrow` is supplied. */
export function PartFilamentRequirementsList({
  requirements,
  onNarrow,
  onNarrowToProfile,
  layout = 'list',
}: PartFilamentRequirementsListProps) {
  const filamentsQuery = useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint({
    query: { enabled: true },
  });
  const filamentsData = filamentsQuery.data?.data as
    | ListFilamentsResponse
    | undefined;
  const filaments = (filamentsData?.filaments ?? []) as FilamentResponse[];

  if (requirements.length === 0) {
    return (
      <EmptyState title="No filament requirements" size="sm" bordered={false} />
    );
  }

  const variants = filaments.flatMap((f) =>
    (f.variants ?? []).map((v) => ({
      ...v,
      name: `${f.vendorName} ${f.material} · ${v.colorName}`,
    })),
  );

  return (
    <ul
      className={
        layout === 'grid'
          ? 'grid grid-cols-1 gap-x-6 gap-y-3 lg:grid-cols-2'
          : 'flex flex-col gap-3'
      }
    >
      {requirements.map((requirement) => (
        <li key={requirement.key} className="flex flex-col gap-2">
          <div className="flex flex-wrap items-center gap-2">
            <span
              className="size-4 shrink-0 rounded-full border"
              style={{
                backgroundColor:
                  variants.find(
                    (v) => v.id === requirement.narrowFilamentVariantId,
                  )?.colorHex ??
                  requirement.detectedColorHex ??
                  undefined,
              }}
              aria-hidden
            />
            <Badge variant="secondary">{requirementLabel(requirement)}</Badge>
            {requirement.estimatedGrams != null && (
              <span className="font-mono text-sm tabular-nums">
                {formatGrams(requirement.estimatedGrams)}
              </span>
            )}
            {(onNarrow || requirement.narrowFilamentVariantId) && (
              <ArrowRightIcon
                className="size-4 shrink-0 text-muted-foreground"
                aria-hidden
              />
            )}
            {onNarrow ? (
              <Select
                value={requirement.narrowFilamentVariantId ?? NO_NARROW_VALUE}
                onValueChange={(value) =>
                  onNarrow(
                    requirement.key,
                    value === NO_NARROW_VALUE ? null : value,
                  )
                }
                disabled={filamentsQuery.isLoading || filamentsQuery.isError}
              >
                <SelectTrigger
                  className="w-full sm:w-auto sm:min-w-64 sm:flex-1"
                  aria-label={`Production filament for ${requirementLabel(requirement)}`}
                >
                  <SelectValue placeholder="Choose catalog filament" />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value={NO_NARROW_VALUE}>
                      Use file material and color
                    </SelectItem>
                    {requirement.narrowFilamentVariantId &&
                      !variants.some(
                        (v) => v.id === requirement.narrowFilamentVariantId,
                      ) && (
                        <SelectItem value={requirement.narrowFilamentVariantId}>
                          Filament unavailable
                        </SelectItem>
                      )}
                    {filaments
                      .filter(
                        (f) =>
                          !requirement.materialType ||
                          f.materialType === requirement.materialType,
                      )
                      .flatMap((filament) =>
                        (filament.variants ?? []).map((variant) => (
                          <SelectItem key={variant.id} value={variant.id ?? ''}>
                            {filament.vendorName} {filament.material} ·{' '}
                            {variant.colorName}
                          </SelectItem>
                        )),
                      )}
                  </SelectGroup>
                </SelectContent>
              </Select>
            ) : requirement.narrowFilamentVariantId ? (
              <span className="text-sm">
                {variants.find(
                  (v) => v.id === requirement.narrowFilamentVariantId,
                )?.name ?? 'Filament unavailable'}
              </span>
            ) : null}
            {requirement.narrowFilamentVariantUnresolved && (
              <Badge variant="destructive">Narrowed filament removed</Badge>
            )}
            {requirement.narrowFilamentProfileUnresolved && (
              <Badge variant="destructive">Narrowed profile removed</Badge>
            )}
          </div>
          {onNarrow && filamentsQuery.isError && (
            <p role="alert" className="text-sm text-destructive">
              Could not load filament catalog.
            </p>
          )}
          {requirement.narrowFilamentProfileId && (
            <div className="flex flex-col gap-2 rounded-md bg-muted/40 p-3">
              <span className="text-xs font-medium">
                Existing direct profile constraint
              </span>
              <ProfilePickerCombobox
                kind="filament"
                value={requirement.narrowFilamentProfileId}
                disabled={!onNarrowToProfile}
                onChange={(id) =>
                  onNarrowToProfile?.(String(requirement.key), id)
                }
                placeholder="Profile unavailable"
                materialType={requirement.materialType ?? undefined}
              />
              {onNarrowToProfile && (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    onNarrowToProfile(String(requirement.key), null)
                  }
                >
                  Clear profile constraint
                </Button>
              )}
              <p className="text-xs text-muted-foreground">
                Selecting a catalog filament replaces this constraint. Its
                printer-specific profile is resolved from the loaded filament.
              </p>
            </div>
          )}
        </li>
      ))}
    </ul>
  );
}
