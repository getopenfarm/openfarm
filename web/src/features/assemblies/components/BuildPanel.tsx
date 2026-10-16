import { useQueries } from '@tanstack/react-query';
import { getOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpointQueryOptions } from '@/lib/api/generated/assemblies/assemblies';
import { formatMinutes } from '@/lib/formatMinutes';
import { formatCost, formatGrams } from '@/lib/formatUnits';
import { cn } from '@/lib/utils';
import type { EditableLine } from './BomLineEditor';
import { hardwareCost, printTotals, rollUp } from './bomRollup';
import { useBomCatalog } from './useBomCatalog';

interface BuildPanelProps {
  /** The form's draft lines — the preview never waits for a save. */
  lines: EditableLine[];
  className?: string;
}

const plural = (n: number, one: string, many: string) =>
  `${n} ${n === 1 ? one : many}`;

/**
 * Live build totals for the draft BOM — totals only; the structure tree is the per-line view.
 * Direct lines roll up locally; each distinct sub-assembly is exploded server-side at quantity 1
 * (cached per id) and scaled by its line qty.
 * ponytail: a sub-assembly whose saved tree contains this assembly inflates the preview up to the
 * CTE depth cap (10); save rejects the cycle anyway, so no client-side cycle check.
 */
export function BuildPanel({ lines, className }: BuildPanelProps) {
  const catalog = useBomCatalog();

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
  const incomplete = subPending || subFailed > 0;

  const total = rollUp(lines, subExplosions);
  const print = printTotals(total.parts, catalog.parts);
  const cost = hardwareCost(total.components, catalog.components);

  const notes = [
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
      aria-busy={subPending}
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
            {formatMinutes(print.minutes)}
          </span>
        </div>

        <dl className="flex flex-col gap-1.5 text-sm">
          <Row label="Filament" value={formatGrams(print.grams)} />
          <Row label="Hardware cost" value={formatCost(cost.total)} />
        </dl>
      </div>

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
