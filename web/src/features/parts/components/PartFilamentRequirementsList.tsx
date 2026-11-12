import { ArrowRightIcon, ChevronsUpDownIcon } from 'lucide-react';
import { useState } from 'react';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { Swatch } from '@/components/swatch/Swatch';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Dialog, DialogTrigger } from '@/components/ui/dialog';
import { ProfilePickerCombobox } from '@/features/profiles/components/ProfilePickerCombobox';
import type {
  FilamentResponse,
  ListFilamentsResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint } from '@/lib/api/generated/filaments/filaments';
import { formatGrams } from '@/lib/formatUnits';
import {
  catalogLabel,
  type FilamentChoice,
  FilamentChoiceDialog,
  genericLabel,
  hex,
} from './FilamentChoiceDialog';

export type { FilamentChoice } from './FilamentChoiceDialog';

export interface DisplayFilamentRequirement {
  /** Stable key — `id` for a saved Part's requirement, `requirementIndex` for a still-in-review
   * job preview (research.md §3a/§7). */
  key: string | number;
  materialType: string | null | undefined;
  detectedColorHex: string | null | undefined;
  detectedSubBrand: string | null | undefined;
  narrowFilamentVariantId?: string | null;
  narrowFilamentVariantUnresolved?: boolean;
  /** Any color of this catalog filament. */
  narrowFilamentId?: string | null;
  narrowFilamentUnresolved?: boolean;
  /** Any color of the material, any brand. */
  anyColor?: boolean;
  /** Only meaningful together with onNarrowToProfile (PartDetailPage, US4). */
  narrowFilamentProfileId?: string | null;
  narrowFilamentProfileUnresolved?: boolean;
  /** Per plate set, purge included. */
  estimatedGrams?: number | null;
}

interface PartFilamentRequirementsListProps {
  requirements: DisplayFilamentRequirement[];
  /** Shared catalog filament choice for preview, saved parts and queued jobs. */
  onNarrow?: (key: string | number, choice: FilamentChoice) => void;
  /** Allows changing or clearing an existing legacy direct profile constraint. */
  onNarrowToProfile?: (
    requirementId: string,
    filamentProfileId: string | null,
  ) => void;
  /** `grid` lays requirements out two-up on wide screens. */
  layout?: 'list' | 'grid';
}

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
      name: catalogLabel(f, v.colorName),
    })),
  );

  /** What the requirement prints with, as the trigger and the read-only view show it. */
  function chosen(requirement: DisplayFilamentRequirement) {
    if (requirement.narrowFilamentVariantId) {
      const variant = variants.find(
        (v) => v.id === requirement.narrowFilamentVariantId,
      );
      return variant
        ? { label: variant.name, colorHex: hex(variant.colorHex) }
        : { label: 'Filament unavailable' };
    }
    if (requirement.narrowFilamentId) {
      const filament = filaments.find(
        (f) => f.id === requirement.narrowFilamentId,
      );
      return filament
        ? { label: catalogLabel(filament), auto: true }
        : { label: 'Filament unavailable' };
    }
    const anyColor = requirement.anyColor || !requirement.detectedColorHex;
    return {
      label: genericLabel(
        requirement.materialType,
        requirement.detectedColorHex,
        requirement.anyColor,
      ),
      colorHex: anyColor ? undefined : hex(requirement.detectedColorHex),
      auto: anyColor,
    };
  }

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
                  hex(
                    variants.find(
                      (v) => v.id === requirement.narrowFilamentVariantId,
                    )?.colorHex,
                  ) ??
                  hex(requirement.detectedColorHex) ??
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
            {(onNarrow ||
              requirement.narrowFilamentVariantId ||
              requirement.narrowFilamentId ||
              requirement.anyColor) && (
              <ArrowRightIcon
                className="size-4 shrink-0 text-muted-foreground"
                aria-hidden
              />
            )}
            {onNarrow ? (
              <FilamentChoiceButton
                requirement={requirement}
                requirementLabel={requirementLabel(requirement)}
                current={chosen(requirement)}
                filaments={filaments}
                disabled={filamentsQuery.isLoading || filamentsQuery.isError}
                onChange={(choice) => onNarrow(requirement.key, choice)}
              />
            ) : requirement.narrowFilamentVariantId ||
              requirement.narrowFilamentId ||
              requirement.anyColor ? (
              <span className="text-sm">{chosen(requirement).label}</span>
            ) : null}
            {(requirement.narrowFilamentVariantUnresolved ||
              requirement.narrowFilamentUnresolved) && (
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

function FilamentChoiceButton({
  requirement,
  requirementLabel,
  current,
  filaments,
  disabled,
  onChange,
}: {
  requirement: DisplayFilamentRequirement;
  requirementLabel: string;
  current: { label: string; colorHex?: string; auto?: boolean };
  filaments: FilamentResponse[];
  disabled: boolean;
  onChange: (choice: FilamentChoice) => void;
}) {
  const [open, setOpen] = useState(false);
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button
          type="button"
          variant="outline"
          disabled={disabled}
          className="w-full justify-start sm:w-auto sm:min-w-64 sm:flex-1"
          aria-label={`Production filament for ${requirementLabel}: ${current.label}`}
        >
          <Swatch colorHex={current.colorHex} auto={current.auto} />
          <span className="flex-1 truncate text-left">{current.label}</span>
          <ChevronsUpDownIcon className="text-muted-foreground" aria-hidden />
        </Button>
      </DialogTrigger>
      {/* Mounted only while open, so each open starts from the saved choice. */}
      {open && (
        <FilamentChoiceDialog
          requirementLabel={requirementLabel}
          materialType={requirement.materialType}
          detectedColorHex={requirement.detectedColorHex}
          filaments={filaments}
          value={{
            variantId: requirement.narrowFilamentVariantId ?? null,
            filamentId: requirement.narrowFilamentId ?? null,
            anyColor: requirement.anyColor ?? false,
          }}
          onValueChange={onChange}
        />
      )}
    </Dialog>
  );
}
