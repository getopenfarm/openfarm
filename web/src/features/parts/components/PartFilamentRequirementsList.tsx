import { Badge } from '@/components/ui/badge';
import {
  Select,
  SelectContent,
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
  /** Present only in the review dialog — lets the operator attach/change a narrower
   * Filament Profile/color-variant override per requirement (FR-012a). Absent = read-only
   * display, used on PartDetailPage where narrowing already happened at confirm time. */
  onNarrow?: (key: string | number, filamentVariantId: string | null) => void;
  /**
   * Present only on PartDetailPage — the standalone narrow/change/clear control reachable at any
   * time after confirm (FR-012, US4), calling `PATCH .../filament-requirements/{requirementId}`
   * with the requirement's own real id (never `requirementIndex`). Distinct from `onNarrow` above,
   * which only ever narrows to a FilamentVariant color pick at Confirm time; this narrows to the
   * combined System+User FilamentProfile catalog via `ProfilePickerCombobox`.
   */
  onNarrowToProfile?: (
    requirementId: string,
    filamentProfileId: string | null,
  ) => void;
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
}: PartFilamentRequirementsListProps) {
  const filamentsQuery = useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint({
    query: { enabled: Boolean(onNarrow) },
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
    <ul className="flex flex-col gap-2">
      {requirements.map((requirement) => (
        <li
          key={requirement.key}
          className="flex flex-wrap items-center gap-2 rounded-lg border p-2"
        >
          <span
            className="size-3 shrink-0 rounded-full border"
            style={{
              backgroundColor: requirement.detectedColorHex ?? undefined,
            }}
            aria-hidden
          />
          <Badge variant="secondary">{requirementLabel(requirement)}</Badge>
          {requirement.narrowFilamentVariantUnresolved ? (
            <Badge variant="destructive">Narrowed filament deleted</Badge>
          ) : null}
          {requirement.narrowFilamentProfileUnresolved ? (
            <Badge variant="destructive">Narrowed profile deleted</Badge>
          ) : null}
          {onNarrowToProfile ? (
            <div className="ml-auto w-64">
              <ProfilePickerCombobox
                kind="filament"
                value={requirement.narrowFilamentProfileId ?? null}
                onChange={(filamentProfileId) =>
                  onNarrowToProfile(String(requirement.key), filamentProfileId)
                }
                placeholder="Match by generic type only"
                searchPlaceholder="Search filament profiles…"
                materialType={requirement.materialType ?? undefined}
              />
            </div>
          ) : onNarrow ? (
            <Select
              value={requirement.narrowFilamentVariantId ?? NO_NARROW_VALUE}
              onValueChange={(value) =>
                onNarrow(
                  requirement.key,
                  value === NO_NARROW_VALUE ? null : value,
                )
              }
            >
              <SelectTrigger size="sm" className="ml-auto w-56">
                <SelectValue placeholder="Narrow to a specific filament" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_NARROW_VALUE}>
                  Match by generic type only
                </SelectItem>
                {filaments.flatMap((filament) =>
                  (filament.variants ?? []).map((variant) => (
                    <SelectItem key={variant.id} value={variant.id ?? ''}>
                      {filament.vendorName} {filament.material}{' '}
                      {variant.colorName}
                    </SelectItem>
                  )),
                )}
              </SelectContent>
            </Select>
          ) : null}
        </li>
      ))}
    </ul>
  );
}
