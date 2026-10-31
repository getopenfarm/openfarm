import { TriangleAlertIcon } from 'lucide-react';
import { useId, useState } from 'react';
import { Switch } from '@/components/ui/switch';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import type {
  PermissionArea,
  PermissionDefinition,
} from '@/lib/api/generated/endpoints.schemas';
import { cn } from '@/lib/utils';
import {
  type Ability,
  type AccessLevel,
  describeArea,
  explicitLeaves,
  LEVEL_LABELS,
  levelLeaves,
  levelledLeaves,
  levelOf,
  levelsFor,
} from './describe';

const BLOCKED = 'A group already grants more than this.';

/**
 * The permission catalog as one row per area, each with an access level
 * (None, View, Edit, Full) or Custom, which opens a switch per ability. Under
 * each area a sentence says what the choice allows.
 *
 * `value` is the keys chosen; anything that is not exactly a level shows as
 * Custom. Explicit abilities are never part of a level and always have their
 * own switch. `inherited` is already granted elsewhere (a user's groups):
 * shown on, locked, and never emitted. `onChange` gets keys in catalog order.
 * Administrator-only areas are skipped.
 */
export function AccessEditor({
  areas,
  permissions,
  value,
  onChange,
  inherited = [],
  disabled = false,
}: {
  areas: PermissionArea[];
  permissions: PermissionDefinition[];
  value: string[];
  onChange: (keys: string[]) => void;
  inherited?: string[];
  disabled?: boolean;
}) {
  const id = useId();
  /** Areas whose Custom panel was opened by hand, while still on a level. */
  const [opened, setOpened] = useState<ReadonlySet<string>>(new Set());
  const chosen = new Set(value);
  const fromGroup = new Set(inherited);
  const held = (key: string) => chosen.has(key) || fromGroup.has(key);
  const rows = (areas as Required<PermissionArea>[])
    .filter((area) => !area.administratorOnly)
    .map((area) => ({
      area,
      leaves: (permissions as Ability[]).filter((p) => p.area === area.key),
    }));

  function setOpen(area: string, open: boolean) {
    setOpened((prev) => {
      const next = new Set(prev);
      if (open) next.add(area);
      else next.delete(area);
      return next;
    });
  }

  /** Emits the chosen keys in catalog order. */
  function emit(isOn: (key: string) => boolean) {
    onChange(
      rows
        .flatMap((row) => row.leaves)
        .map((leaf) => leaf.key)
        .filter((key) => !fromGroup.has(key) && isOn(key)),
    );
  }

  function isBlocked(leaves: Ability[], level: AccessLevel) {
    const want = new Set(levelLeaves(leaves, level).map((l) => l.key));
    return levelledLeaves(leaves).some(
      (leaf) => fromGroup.has(leaf.key) && !want.has(leaf.key),
    );
  }

  function pickLevel(area: string, leaves: Ability[], level: AccessLevel) {
    if (disabled || isBlocked(leaves, level)) return;
    const own = new Set(levelledLeaves(leaves).map((l) => l.key));
    const want = new Set(levelLeaves(leaves, level).map((l) => l.key));
    setOpen(area, false);
    emit((key) => (own.has(key) ? want.has(key) : chosen.has(key)));
  }

  function toggle(leaf: Ability, on: boolean) {
    if (disabled || fromGroup.has(leaf.key)) return;
    // Keep the panel open: a switch that happens to land on a level must not
    // fold the switches away under the pointer.
    if (!leaf.isExplicit) setOpen(leaf.area, true);
    emit((key) => (key === leaf.key ? on : chosen.has(key)));
  }

  function ability(area: Required<PermissionArea>, leaf: Ability) {
    const on = held(leaf.key);
    const locked = disabled || fromGroup.has(leaf.key);
    return (
      <label
        key={leaf.key}
        htmlFor={`${id}-${leaf.key}`}
        className={cn(
          'flex cursor-pointer items-center justify-between gap-3 py-[7px]',
          locked && 'cursor-default',
          leaf.isExplicit &&
            'rounded-lg border px-3 py-[9px] transition-colors has-[button[aria-checked=true]]:border-amber has-[button[aria-checked=true]]:bg-amber-soft',
          leaf.isExplicit &&
            !locked &&
            !on &&
            'hover:border-primary hover:bg-accent',
        )}
      >
        <span className="flex min-w-0 flex-col">
          <span className="inline-flex items-center gap-1.5 text-[13px] font-medium">
            {leaf.label}
            {leaf.isSensitive && (
              <span
                className="inline-flex text-amber"
                title="Handle with care: read the description."
              >
                <TriangleAlertIcon className="size-3" />
              </span>
            )}
            {fromGroup.has(leaf.key) && (
              <span className="rounded-full border bg-card px-[7px] text-[11px] font-medium text-muted-foreground">
                From a group
              </span>
            )}
          </span>
          <span className="text-xs leading-[1.4] text-muted-foreground">
            {leaf.description}
          </span>
        </span>
        <Switch
          id={`${id}-${leaf.key}`}
          size="sm"
          checked={on}
          disabled={locked}
          onCheckedChange={(next) => toggle(leaf, next)}
          aria-label={`${area.label}: ${leaf.label}`}
        />
      </label>
    );
  }

  return (
    <div className="@container divide-y rounded-[10px] border bg-card">
      {rows.map(({ area, leaves }) => {
        const holding = leaves.filter((leaf) => held(leaf.key));
        const level = levelOf(
          leaves,
          holding.map((l) => l.key),
        );
        const custom = level === 'custom' || opened.has(area.key);
        const levels = levelsFor(leaves);
        const levelled = levelledLeaves(leaves);
        const explicit = explicitLeaves(leaves);
        const isNone =
          level === 'none' && !custom && !explicit.some((l) => held(l.key));
        return (
          <div key={area.key} className="px-3.5 py-3">
            <div className="flex items-center justify-between gap-4 @max-[600px]:flex-col @max-[600px]:items-stretch @max-[600px]:gap-2">
              <div className="min-w-0">
                <div
                  className={cn(
                    'text-[13.5px] font-semibold',
                    isNone && 'font-medium text-muted-foreground',
                  )}
                >
                  {area.label}
                </div>
                <div
                  className="mt-0.5 text-[12.5px] leading-[1.45] text-muted-foreground"
                  aria-live="polite"
                >
                  {describeArea(
                    leaves,
                    area.noun,
                    holding.map((l) => l.key),
                  )}
                </div>
              </div>
              {levels.length > 1 && (
                <ToggleGroup
                  type="single"
                  role="radiogroup"
                  aria-label={`${area.label} access`}
                  spacing={0.5}
                  disabled={disabled}
                  value={custom ? 'custom' : level}
                  onValueChange={(next) => {
                    // Radix sends "" when the active item is clicked again:
                    // that only matters for an opened Custom panel, to fold it.
                    if (next === '') {
                      if (custom && level !== 'custom')
                        setOpen(area.key, false);
                    } else if (next === 'custom') {
                      if (level !== 'custom') setOpen(area.key, true);
                    } else pickLevel(area.key, leaves, next as AccessLevel);
                  }}
                  className="shrink-0 rounded-[9px] bg-muted p-0.5 @max-[600px]:flex-wrap @max-[600px]:self-start"
                >
                  {levels.map((l) => {
                    const blocked = isBlocked(leaves, l);
                    return (
                      <ToggleGroupItem
                        key={l}
                        value={l}
                        disabled={disabled || blocked}
                        title={blocked ? BLOCKED : undefined}
                        className={segment}
                      >
                        {LEVEL_LABELS[l]}
                      </ToggleGroupItem>
                    );
                  })}
                  {levelled.length > 1 && (
                    <ToggleGroupItem value="custom" className={segment}>
                      {LEVEL_LABELS.custom}
                    </ToggleGroupItem>
                  )}
                </ToggleGroup>
              )}
            </div>
            {custom && (
              <div className="mt-2.5 grid grid-cols-2 gap-x-5 gap-y-0.5 rounded-lg bg-muted px-3 py-2 @max-[600px]:grid-cols-1">
                {levelled.map((leaf) => ability(area, leaf))}
              </div>
            )}
            {explicit.length > 0 && (
              <div className="mt-2.5 flex flex-col gap-1.5">
                {explicit.map((leaf) => ability(area, leaf))}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

const segment =
  'h-auto min-w-0 rounded-[7px] px-2.5 py-1 text-[12.5px] font-medium text-muted-foreground hover:bg-transparent hover:text-foreground data-[state=on]:bg-card data-[state=on]:text-foreground data-[state=on]:shadow-sm disabled:pointer-events-auto disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:text-muted-foreground';
