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
import { categories } from '@/features/cost-catalog/costing';
import { useCurrency } from '@/features/settings/generalSettings';
import type { AssemblyCostInput } from '@/lib/api/generated/endpoints.schemas';
import { formatMinutes } from '@/lib/formatMinutes';
import { formatCost, formatGrams } from '@/lib/formatUnits';
import type { EditableLine } from './BomLineEditor';
import { filamentColorKey } from './bomRollup';
import { useBuildEstimate } from './useBuildEstimate';

interface BuildPanelProps {
  /** The form's draft lines — the preview never waits for a save. */
  lines: EditableLine[];
  className?: string;
  additionalCosts?: AssemblyCostInput[];
}

/** Live build totals for the draft BOM, filament split by colour (purge included); the structure tree is the per-line view. */
export function BuildPanel({
  lines,
  additionalCosts = [],
  className,
}: BuildPanelProps) {
  const currency = useCurrency();
  const {
    catalog,
    machineQuery,
    filamentQuery,
    subQueries,
    loading,
    failed,
    incomplete,
    total,
    print,
    machine,
    machineUnavailable,
    breakdown,
    shipping,
    costsPartial,
    variants,
    filament,
    failurePercent,
    failure,
    printPartial,
    failureAvailable,
    notes,
    totalCost,
  } = useBuildEstimate(lines, additionalCosts);
  const estimate = (amount: number, partial: boolean, available = true) =>
    incomplete || !available
      ? 'Unavailable'
      : `${formatCost(amount, currency)}${partial ? ' (partial)' : ''}`;

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
              label="Printer depreciation"
              value={estimate(
                machine.depreciation.total,
                machine.depreciation.missing > 0,
                !machineUnavailable &&
                  (machine.depreciation.priced > 0 || total.parts.size === 0),
              )}
              loading={loading}
            />
            <Row
              label="Electricity"
              value={estimate(
                machine.electricity.total,
                machine.electricity.missing > 0,
                !machineUnavailable &&
                  (machine.electricity.priced > 0 || total.parts.size === 0),
              )}
              loading={loading}
            />
            {failurePercent > 0 && (
              <Row
                label={`Failure allowance (${failurePercent}%)`}
                value={estimate(failure, printPartial, failureAvailable)}
                loading={loading}
              />
            )}
            {categories
              .filter(
                (category) =>
                  category !== 'Shipping' &&
                  (category === 'Hardware' || breakdown.has(category)),
              )
              .map((category) => (
                <Row
                  key={category}
                  label={`${category === 'Labour' ? 'Labor' : category} cost`}
                  value={estimate(breakdown.get(category) ?? 0, costsPartial)}
                  loading={loading}
                />
              ))}
            <Row
              label="Product cost"
              value={estimate(totalCost.amount - shipping, totalCost.partial)}
              loading={loading}
            />
            <Row
              label="Shipping allowance"
              value={estimate(shipping, costsPartial)}
              loading={loading}
            />
          </dl>
        </CardContent>
        <CardFooter>
          <dl className="w-full" aria-live="polite">
            <Row
              label="Estimated total including shipping"
              value={estimate(
                totalCost.amount,
                totalCost.partial,
                totalCost.available,
              )}
              loading={loading}
              prominent
            />
          </dl>
        </CardFooter>
      </Card>
      {machineQuery.isError && total.parts.size > 0 && (
        <Button variant="outline" onClick={() => void machineQuery.refetch()}>
          Retry printer costs
        </Button>
      )}
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
