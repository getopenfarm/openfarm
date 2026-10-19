import { useQueries } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { getOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpointQueryOptions } from '@/lib/api/generated/assemblies/assemblies';
import { useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint } from '@/lib/api/generated/filaments/filaments';
import { formatMinutes } from '@/lib/formatMinutes';
import { formatCost, formatGrams } from '@/lib/formatUnits';
import { cn } from '@/lib/utils';
import type { EditableLine } from './BomLineEditor';
import {
  filamentColorKey,
  filamentCost,
  hardwareCost,
  printTotals,
  rollUp,
} from './bomRollup';
import { useBomCatalog } from './useBomCatalog';

interface BuildPanelProps {
  /** The form's draft lines — the preview never waits for a save. */
  lines: EditableLine[];
  className?: string;
}

const plural = (n: number, one: string, many: string) =>
  `${n} ${n === 1 ? one : many}`;

/**
 * Live build totals for the draft BOM, filament split by colour (purge included); the structure tree is the per-line view.
 * Direct lines roll up locally; each distinct sub-assembly is exploded server-side at quantity 1
 * (cached per id) and scaled by its line qty.
 * ponytail: a sub-assembly whose saved tree contains this assembly inflates the preview up to the
 * CTE depth cap (10); save rejects the cycle anyway, so no client-side cycle check.
 */
export function BuildPanel({ lines, className }: BuildPanelProps) {
  const catalog = useBomCatalog();
  const filamentQuery = useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint();

  const subIds = [
    ...new Set(
      lines.filter((l) => l.targetKind === 'Assembly').map((l) => l.targetId),
    ),
  ];
  const subQueries = useQueries({
    queries: subIds.map((id) =>
      getOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpointQueryOptions(id, {
        quantity: 1,
      }),
    ),
  });
  const subExplosions = new Map(
    subIds.map((id, i) => [id, subQueries[i]?.data?.data || undefined]),
  );
  const subFailed = subQueries.filter((q) => q.isError).length;
  const subPending = subQueries.some((q) => q.isPending);
  // Missing explosions drop parts from every total, so nothing reads as final until all load.
  const loading = subPending || catalog.isPending || filamentQuery.isPending;
  const failed = subFailed > 0 || catalog.isError || filamentQuery.isError;
  const incomplete = loading || failed;

  const total = rollUp(lines, subExplosions);
  const print = printTotals(total.parts, catalog.parts);
  const cost = hardwareCost(total.components, catalog.components);
  const filament = filamentCost(
    total.parts,
    catalog.parts,
    filamentQuery.data?.data?.filaments ?? [],
  );
  const estimate = (amount: number, partial: boolean, available = true) =>
    incomplete || !available
      ? 'Unavailable'
      : `${formatCost(amount)}${partial ? ' (partial)' : ''}`;

  const notes = [
    (catalog.isPending || filamentQuery.isPending) && 'Loading catalog…',
    (catalog.isError || filamentQuery.isError) &&
      'Could not load catalog. Totals incomplete.',
    filament.missingSelection > 0 &&
      'Select catalog filament on parts to estimate all filament costs.',
    filament.missingPrice > 0 &&
      'Selected filament has no price. Filament cost incomplete.',
    filament.missingGrams > 0 &&
      'Filament weight estimates unavailable. Filament cost incomplete.',
    subPending && 'Loading sub-assemblies…',
    subFailed > 0 &&
      `${plural(subFailed, 'sub-assembly', 'sub-assemblies')} failed to load. Totals incomplete.`,
    print.unestimated > 0 &&
      `${plural(print.unestimated, 'part', 'parts')} not estimated.`,
    cost.missingCost > 0 &&
      `${plural(cost.missingCost, 'component', 'components')} without cost.`,
  ].filter((n): n is string => Boolean(n));

  return (
    <aside
      aria-labelledby="build-heading"
      aria-busy={loading}
      className={cn('rounded-lg border bg-card', className)}
    >
      <div className="flex items-center gap-3 border-b px-4 py-3">
        <h3 id="build-heading" className="font-medium">
          Build
        </h3>
      </div>

      <div
        aria-live="polite"
        className={cn(
          'flex flex-col gap-4 px-4 py-4',
          incomplete && 'opacity-60',
        )}
      >
        <div className="flex flex-col">
          <span className="text-sm text-muted-foreground">Print time</span>
          <span className="font-heading text-3xl font-semibold tabular-nums">
            {incomplete ? 'Unavailable' : formatMinutes(print.minutes)}
          </span>
        </div>

        <div className="flex flex-col gap-1.5 text-sm">
          <dl>
            <Row
              label="Filament"
              value={incomplete ? 'Unavailable' : formatGrams(print.grams)}
            />
          </dl>
          {print.colors.some((c) => c.colorHex) && (
            <ul aria-label="Filament by colour" className="flex flex-col gap-1">
              {print.colors.map((c) => (
                <li
                  key={`${c.materialType}|${c.colorHex}`}
                  className="flex items-center justify-between gap-3 pl-3 text-muted-foreground"
                >
                  <span className="flex items-center gap-2">
                    <span
                      className="size-3 shrink-0 rounded-full border"
                      style={{ backgroundColor: c.colorHex ?? undefined }}
                      aria-hidden
                    />
                    {c.colorHex
                      ? `${c.materialType ?? 'Any'} ${c.colorHex}`
                      : 'Unknown colour'}
                  </span>
                  <span className="text-right tabular-nums">
                    {formatGrams(c.grams)}
                    <span className="block text-xs">
                      {(() => {
                        const color = filament.colors.get(
                          filamentColorKey(c.materialType, c.colorHex),
                        );
                        return estimate(
                          color?.total ?? 0,
                          (color?.missing ?? 0) > 0,
                          (color?.priced ?? 0) > 0,
                        );
                      })()}
                    </span>
                  </span>
                </li>
              ))}
            </ul>
          )}
          <dl>
            <Row
              label="Filament cost"
              value={estimate(
                filament.total,
                filament.incomplete,
                filament.priced > 0 || total.parts.size === 0,
              )}
            />
            <Row
              label="Hardware cost"
              value={estimate(cost.total, cost.missingCost > 0)}
            />
            <Row
              label="Estimated total"
              value={estimate(
                filament.total + cost.total,
                filament.incomplete || cost.missingCost > 0,
                filament.priced > 0 ||
                  total.components.size > cost.missingCost ||
                  (total.parts.size === 0 && total.components.size === 0),
              )}
            />
          </dl>
        </div>
      </div>

      {failed && (
        <div className="px-4 pb-3">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => {
              catalog.retry();
              void filamentQuery.refetch();
              for (const query of subQueries) void query.refetch();
            }}
          >
            Retry totals
          </Button>
        </div>
      )}
      {notes.length > 0 && (
        <ul className="flex flex-col gap-1 border-t px-4 py-3 text-xs text-muted-foreground">
          {notes.map((note) => (
            <li key={note}>{note}</li>
          ))}
        </ul>
      )}
    </aside>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-medium tabular-nums">{value}</dd>
    </div>
  );
}
