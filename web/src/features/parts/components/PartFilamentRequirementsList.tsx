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
}

interface PartFilamentRequirementsListProps {
  requirements: DisplayFilamentRequirement[];
  /** Shared catalog colour-variant selector for preview, saved parts and queued jobs. */
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
      <p className="text-sm text-muted-foreground">No filament requirements.</p>
    );
  }

  return (
    <ul
      className={
        layout === 'grid'
          ? 'grid grid-cols-1 gap-3 lg:grid-cols-2'
          : 'flex flex-col gap-2'
      }
    >
      {requirements.map((requirement) => (
        <li
          key={requirement.key}
          className="flex flex-col gap-3 rounded-lg border p-4"
        >
          <div className="flex items-center gap-2">
            <span
              className="size-4 shrink-0 rounded-full border"
              style={{
                backgroundColor:
                  filaments
                    .flatMap((f) => f.variants ?? [])
                    .find((v) => v.id === requirement.narrowFilamentVariantId)
                    ?.colorHex ??
                  requirement.detectedColorHex ??
                  undefined,
              }}
              aria-hidden
            />
            <Badge variant="secondary">{requirementLabel(requirement)}</Badge>
            <span className="text-xs text-muted-foreground">
              File colour: {requirement.detectedColorHex ?? 'Unspecified'}
            </span>
          </div>
          {requirement.narrowFilamentVariantUnresolved ? (
            <Badge variant="destructive">Narrowed filament deleted</Badge>
          ) : null}
          {requirement.narrowFilamentProfileUnresolved ? (
            <Badge variant="destructive">Narrowed profile deleted</Badge>
          ) : null}
          {onNarrow ? (
            <div className="flex flex-col gap-1.5">
              <span className="text-sm font-medium">Production filament</span>
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
                  className="w-full"
                  aria-label="Production filament"
                >
                  <SelectValue placeholder="Choose catalog filament" />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value={NO_NARROW_VALUE}>
                      Use file material and colour
                    </SelectItem>
                    {requirement.narrowFilamentVariantId &&
                      !filaments
                        .flatMap((f) => f.variants ?? [])
                        .some(
                          (v) => v.id === requirement.narrowFilamentVariantId,
                        ) && (
                        <SelectItem value={requirement.narrowFilamentVariantId}>
                          Selected filament unavailable
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
              <p className="text-xs text-muted-foreground">
                A catalog choice matches a loaded spool of that exact colour
                variant, overriding the file colour.
              </p>
              {filamentsQuery.isError && (
                <p role="alert" className="text-sm text-destructive">
                  Could not load the filament catalog.
                </p>
              )}
            </div>
          ) : requirement.narrowFilamentVariantId ? (
            <p className="text-sm">
              {filaments
                .flatMap((f) =>
                  (f.variants ?? []).map((v) => ({
                    ...v,
                    name: `${f.vendorName} ${f.material} · ${v.colorName}`,
                  })),
                )
                .find((v) => v.id === requirement.narrowFilamentVariantId)
                ?.name ?? 'Selected filament unavailable'}
            </p>
          ) : null}
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
                placeholder="Selected profile unavailable"
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
