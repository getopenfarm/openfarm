import { useId, useMemo, useState } from 'react';
import { DataTableSearch } from '@/components/data-table/DataTableSearch';
import { EmptyState } from '@/components/empty-state/EmptyState';
import { Swatch } from '@/components/swatch/Swatch';
import { Button } from '@/components/ui/button';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import type {
  FilamentResponse,
  SpoolResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { formatGrams } from '@/lib/formatUnits';
import { FillMeter } from './FillMeter';

const ALL = 'all';

interface SpoolPickerProps {
  /** The spools returned for the current search, already limited to those in storage. */
  spools: SpoolResponse[];
  /** Spools matching the search across all pages; more than `spools.length` when the list was cut off. */
  total: number;
  catalog: FilamentResponse[];
  /** Search text; the owner sends it to the server, so results arrive already filtered. */
  query: string;
  onQueryChange: (query: string) => void;
  /** The shown spools belong to an earlier search; a fetch for the current text is on its way. */
  refreshing: boolean;
  value: string;
  onValueChange: (spoolId: string) => void;
}

/** Server-side search + material filter over a radio list; native radios give arrow-key navigation for free. */
export function SpoolPicker({
  spools,
  total,
  catalog,
  query,
  onQueryChange,
  refreshing,
  value,
  onValueChange,
}: SpoolPickerProps) {
  const name = useId();
  const [materialType, setMaterialType] = useState(ALL);

  const typeOf = useMemo(() => {
    const byVariant = new Map<string, string>();
    for (const filament of catalog)
      for (const variant of filament.variants ?? [])
        if (variant.id && filament.materialType)
          byVariant.set(variant.id, filament.materialType);
    return (spool: SpoolResponse) =>
      byVariant.get(spool.filamentVariantId ?? '') ?? 'Other';
  }, [catalog]);

  // Counts are only honest when every match is loaded.
  const truncated = total > spools.length;
  const counts = new Map<string, number>();
  for (const spool of spools)
    counts.set(typeOf(spool), (counts.get(typeOf(spool)) ?? 0) + 1);
  const types = [...counts.keys()].sort((a, b) => a.localeCompare(b));

  const visible = spools
    .filter((spool) => materialType === ALL || typeOf(spool) === materialType)
    .sort(
      (a, b) =>
        (a.material ?? '').localeCompare(b.material ?? '') ||
        (a.colorName ?? '').localeCompare(b.colorName ?? ''),
    );

  const filtered = query.trim() !== '' || materialType !== ALL;
  if (spools.length === 0 && !filtered && !refreshing)
    return (
      <EmptyState size="sm" bordered={false} title="No spools in storage" />
    );

  const clearFilters = () => {
    onQueryChange('');
    setMaterialType(ALL);
  };

  return (
    <div className="flex flex-col gap-3">
      <DataTableSearch
        className="sm:w-full"
        value={query}
        onValueChange={onQueryChange}
        label="Search spools"
        placeholder="Search vendor, material or color…"
      />
      {types.length > 1 && (
        <ToggleGroup
          aria-label="Filter spools by material"
          className="max-w-full flex-wrap justify-start"
          type="single"
          variant="outline"
          value={materialType}
          onValueChange={(next) => {
            if (next) setMaterialType(next);
          }}
        >
          <ToggleGroupItem value={ALL}>
            All
            {!truncated && (
              <span className="ml-1 tabular-nums opacity-70">
                {spools.length}
              </span>
            )}
          </ToggleGroupItem>
          {types.map((type) => (
            <ToggleGroupItem key={type} value={type}>
              {type}
              {!truncated && (
                <span className="ml-1 tabular-nums opacity-70">
                  {counts.get(type)}
                </span>
              )}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
      )}
      {visible.length === 0 ? (
        <EmptyState
          size="sm"
          bordered={false}
          title="No spools match"
          action={
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={clearFilters}
            >
              Clear filters
            </Button>
          }
        />
      ) : (
        <fieldset className="m-0 min-w-0 divide-y rounded-lg border p-0">
          <legend className="sr-only">Spools in storage</legend>
          {visible.map((spool) => {
            const capacity = spool.spoolWeightGrams ?? 0;
            const remaining = spool.remainingWeightGrams;
            return (
              <label
                key={spool.id}
                className="flex cursor-pointer items-center gap-3 px-3 py-2.5 first:rounded-t-lg last:rounded-b-lg hover:bg-accent/40 has-checked:bg-accent has-focus-visible:outline-2 has-focus-visible:-outline-offset-2 has-focus-visible:outline-ring"
              >
                <input
                  type="radio"
                  name={name}
                  className="sr-only"
                  checked={value === spool.id}
                  onChange={() => onValueChange(spool.id ?? '')}
                />
                <Swatch colorHex={spool.colorHex} size="md" />
                <span className="flex min-w-0 flex-1 flex-col gap-1">
                  <span className="text-[13px] font-medium break-words">
                    {[spool.vendorName, spool.material]
                      .filter(Boolean)
                      .join(' ')}
                  </span>
                  <span className="text-[11.5px] text-muted-foreground">
                    {spool.colorName}
                  </span>
                  {capacity > 0 && remaining != null && (
                    <FillMeter
                      colorHex={spool.colorHex}
                      percent={(remaining / capacity) * 100}
                    />
                  )}
                </span>
                {remaining != null && (
                  <span className="shrink-0 font-mono text-[11px] text-muted-foreground tabular-nums">
                    {formatGrams(remaining)}
                  </span>
                )}
              </label>
            );
          })}
        </fieldset>
      )}
      {truncated && (
        <p className="text-xs text-muted-foreground">
          Showing {spools.length} of {total}. Search to narrow the list.
        </p>
      )}
    </div>
  );
}
