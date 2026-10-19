import { useQueries } from '@tanstack/react-query';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Separator } from '@/components/ui/separator';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { getOpenFarmApiEndpointsAssembliesExplodeAssemblyEndpointQueryOptions } from '@/lib/api/generated/assemblies/assemblies';
import { useOpenFarmApiEndpointsFilamentsListFilamentsEndpoint } from '@/lib/api/generated/filaments/filaments';
import { formatMinutes } from '@/lib/formatMinutes';
import { formatCost, formatGrams } from '@/lib/formatUnits';
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
  const variants = new Map(
    (filamentQuery.data?.data?.filaments ?? []).flatMap((filament) =>
      (filament.variants ?? []).map(
        (variant) =>
          [
            variant.id,
            {
              name:
                [
                  [filament.vendorName, filament.material]
                    .filter(Boolean)
                    .join(' '),
                  variant.colorName,
                ]
                  .filter(Boolean)
                  .join(' · ') || 'Filament unavailable',
              colorHex: variant.colorHex,
            },
          ] as const,
      ),
    ),
  );
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
      `Could not load ${plural(subFailed, 'sub-assembly', 'sub-assemblies')}. Totals incomplete.`,
    print.unestimated > 0 &&
      `${plural(print.unestimated, 'part', 'parts')} not estimated.`,
    cost.missingCost > 0 &&
      `${plural(cost.missingCost, 'component', 'components')} without cost.`,
  ].filter((n): n is string => Boolean(n));

  return (
    <aside
      aria-labelledby="build-heading"
      aria-busy={loading}
      className={className}
    >
      <Card>
        <CardHeader>
          <CardTitle id="build-heading">Build</CardTitle>
        </CardHeader>
        <CardContent className="flex min-w-0 flex-col gap-4" aria-live="polite">
          <dl className="grid grid-cols-2 gap-4">
            <Metric
              label="Print time"
              value={incomplete ? 'Unavailable' : formatMinutes(print.minutes)}
              loading={loading}
            />
            <Metric
              label="Filament"
              value={incomplete ? 'Unavailable' : formatGrams(print.grams)}
              loading={loading}
            />
          </dl>
          {loading ? (
            <Skeleton
              className="h-24 w-full"
              aria-label="Loading filament breakdown"
            />
          ) : print.colors.length > 0 && !failed ? (
            <Table aria-label="Filament breakdown">
              <TableHeader>
                <TableRow>
                  <TableHead scope="col">Filament</TableHead>
                  <TableHead scope="col" className="text-right">
                    Weight
                  </TableHead>
                  <TableHead scope="col" className="text-right">
                    Cost
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {print.colors.map((c) => {
                  const color = filament.colors.get(
                    filamentColorKey(c.materialType, c.colorHex, c.variantId),
                  );
                  const variant = c.variantId
                    ? variants.get(c.variantId)
                    : undefined;
                  return (
                    <TableRow
                      key={filamentColorKey(
                        c.materialType,
                        c.colorHex,
                        c.variantId,
                      )}
                    >
                      <TableCell className="whitespace-normal">
                        <span className="flex items-center gap-2">
                          <span
                            className="size-3 shrink-0 rounded-full border"
                            style={{
                              backgroundColor:
                                variant?.colorHex ?? c.colorHex ?? undefined,
                            }}
                            aria-hidden
                          />
                          <span>
                            {variant
                              ? variant.name
                              : c.variantId
                                ? 'Filament unavailable'
                                : c.colorHex
                                  ? `${c.materialType ?? 'Any'} ${c.colorHex}`
                                  : 'Unknown color'}
                          </span>
                        </span>
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {formatGrams(c.grams)}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        {estimate(
                          color?.total ?? 0,
                          (color?.missing ?? 0) > 0,
                          (color?.priced ?? 0) > 0,
                        )}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          ) : null}
          <Separator />
          <dl className="flex flex-col gap-2">
            <Row
              label="Filament cost"
              value={estimate(
                filament.total,
                filament.incomplete,
                filament.priced > 0 || total.parts.size === 0,
              )}
              loading={loading}
            />
            <Row
              label="Hardware cost"
              value={estimate(cost.total, cost.missingCost > 0)}
              loading={loading}
            />
          </dl>
        </CardContent>
        <CardFooter>
          <dl className="w-full" aria-live="polite">
            <Row
              label="Estimated total"
              value={estimate(
                filament.total + cost.total,
                filament.incomplete || cost.missingCost > 0,
                filament.priced > 0 ||
                  total.components.size > cost.missingCost ||
                  (total.parts.size === 0 && total.components.size === 0),
              )}
              loading={loading}
              prominent
            />
          </dl>
        </CardFooter>
      </Card>
      {notes.length > 0 && (
        <Alert role="status" className="mt-4">
          <AlertTitle>
            {loading ? 'Calculating build…' : 'Build estimates incomplete'}
          </AlertTitle>
          <AlertDescription>
            <ul className="flex flex-col gap-1">
              {notes.map((note) => (
                <li key={note}>{note}</li>
              ))}
            </ul>
            {failed && (
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={
                  catalog.isFetching ||
                  filamentQuery.isFetching ||
                  subQueries.some((q) => q.isFetching)
                }
                onClick={() => {
                  catalog.retry();
                  void filamentQuery.refetch();
                  for (const query of subQueries) void query.refetch();
                }}
              >
                Retry totals
              </Button>
            )}
          </AlertDescription>
        </Alert>
      )}
    </aside>
  );
}

function Metric({
  label,
  value,
  loading,
}: {
  label: string;
  value: string;
  loading: boolean;
}) {
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-heading text-xl font-semibold break-words tabular-nums">
        {loading ? <Skeleton className="h-7 w-20" /> : value}
      </dd>
    </div>
  );
}

function Row({
  label,
  value,
  loading,
  prominent = false,
}: {
  label: string;
  value: string;
  loading: boolean;
  prominent?: boolean;
}) {
  return (
    <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
      <dt className={prominent ? 'font-medium' : 'text-muted-foreground'}>
        {label}
      </dt>
      <dd
        className={
          prominent
            ? 'font-heading text-xl font-semibold tabular-nums'
            : 'font-medium tabular-nums'
        }
      >
        {loading ? <Skeleton className="h-6 w-20" /> : value}
      </dd>
    </div>
  );
}
