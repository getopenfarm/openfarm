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
import { formatCost } from '@/lib/formatUnits';
import { formatEstimateMetric } from '@/lib/printEstimates';
import type { EditableLine } from './BomLineEditor';
import { type PricingDraftInput, useBuildEstimate } from './useBuildEstimate';

interface BuildPanelProps {
  /** The form's draft lines — the preview never waits for a save. */
  lines: EditableLine[];
  className?: string;
  additionalCosts?: AssemblyCostInput[];
  /** The Pricing draft, so the panel shares the Pricing section's quote request. */
  pricing?: PricingDraftInput;
}

/** Live build totals for the draft BOM, filament split by colour (purge included); the structure tree is the per-line view. */
export function BuildPanel({
  lines,
  additionalCosts = [],
  pricing,
  className,
}: BuildPanelProps) {
  const currency = useCurrency();
  const { quote, loading, failed, incomplete, fetching, retry } =
    useBuildEstimate(lines, additionalCosts, pricing);
  const parts = quote?.parts ?? 0;
  const print = quote?.print;
  const filament = quote?.filament;
  const depreciation = quote?.depreciation;
  const electricity = quote?.electricity;
  const breakdown = new Map(
    (quote?.breakdown ?? []).map((b) => [b.category ?? '', b.amount ?? 0]),
  );
  const shipping = quote?.shipping ?? 0;
  const costsPartial = !!quote?.costsPartial;
  const failurePercent = quote?.failurePercent ?? 0;
  const totalCost = {
    amount: quote?.total?.amount ?? 0,
    partial: !!quote?.total?.partial,
    available: !!quote?.total?.available,
  };
  const notes = failed
    ? ['Could not calculate totals. Retry to complete estimates.']
    : (quote?.notes ?? []);
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
              value={
                incomplete || !print
                  ? 'Unavailable'
                  : formatEstimateMetric(
                      print.time,
                      'time',
                      !!print.approximate,
                    )
              }
              loading={loading}
            />
            <Metric
              label="Filament"
              value={
                incomplete || !print
                  ? 'Unavailable'
                  : formatEstimateMetric(
                      print.filament,
                      'filament',
                      !!print.approximate,
                    )
              }
              loading={loading}
            />
          </dl>
          {loading ? (
            <Skeleton
              className="h-24 w-full"
              aria-label="Loading filament breakdown"
            />
          ) : (quote?.colors ?? []).length > 0 && !incomplete ? (
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
                {(quote?.colors ?? []).map((c) => (
                  <TableRow
                    key={`${c.variantId ?? ''}|${c.materialType ?? ''}|${c.colorHex ?? ''}`}
                  >
                    <TableCell className="whitespace-normal">
                      <span className="flex items-center gap-2">
                        <span
                          className="size-3 shrink-0 rounded-full border"
                          style={{
                            backgroundColor: c.swatchHex ?? undefined,
                          }}
                          aria-hidden
                        />
                        <span>{c.name}</span>
                      </span>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {formatEstimateMetric(
                        { value: c.grams, missing: c.missing },
                        'filament',
                        false,
                      )}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      {estimate(
                        c.cost ?? 0,
                        (c.costMissing ?? 0) > 0,
                        (c.costPriced ?? 0) > 0,
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : null}
          <Separator />
          <dl className="flex flex-col gap-2">
            <Row
              label="Filament cost"
              value={estimate(
                filament?.total ?? 0,
                !!filament?.incomplete,
                (filament?.priced ?? 0) > 0 || parts === 0,
              )}
              loading={loading}
            />
            <Row
              label="Printer depreciation"
              value={estimate(
                depreciation?.total ?? 0,
                (depreciation?.missing ?? 0) > 0,
                (depreciation?.priced ?? 0) > 0 || parts === 0,
              )}
              loading={loading}
            />
            <Row
              label="Electricity"
              value={estimate(
                electricity?.total ?? 0,
                (electricity?.missing ?? 0) > 0,
                (electricity?.priced ?? 0) > 0 || parts === 0,
              )}
              loading={loading}
            />
            {failurePercent > 0 && (
              <Row
                label={`Failure allowance (${failurePercent}%)`}
                value={estimate(
                  quote?.failure ?? 0,
                  !!quote?.printPartial,
                  !!quote?.failureAvailable,
                )}
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
      {notes.length > 0 && (
        <Alert role="status" className="mt-4">
          <AlertTitle>
            {incomplete || totalCost.partial
              ? 'Build estimates incomplete'
              : 'Estimate notes'}
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
                disabled={fetching}
                onClick={retry}
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
