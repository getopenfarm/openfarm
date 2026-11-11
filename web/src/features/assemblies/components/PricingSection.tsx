import { Link } from 'react-router';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
} from '@/components/ui/input-group';
import { MoneyInput } from '@/components/ui/money-input';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { useCurrency } from '@/features/settings/generalSettings';
import type {
  AssemblyCostInput,
  ProfilePricing,
  QuoteProfile,
} from '@/lib/api/generated/endpoints.schemas';
import { formatCost } from '@/lib/formatUnits';
import { cn } from '@/lib/utils';
import type { EditableLine } from './BomLineEditor';
import { useBuildEstimate } from './useBuildEstimate';

export type PricingMode = 'Price' | 'Margin' | 'Markup';
const pricingModes: PricingMode[] = ['Price', 'Margin', 'Markup'];

/** The form keeps the typed text; blank saves no pricing. */
export interface PricingDraft {
  mode: PricingMode;
  value: string;
}

interface PricingSectionProps {
  lines: EditableLine[];
  additionalCosts: AssemblyCostInput[];
  value: PricingDraft;
  onChange: (next: PricingDraft) => void;
  error?: string;
}

const percent = (f: number | null | undefined, digits = 1) =>
  f == null ? '—' : `${(f * 100).toFixed(digits)}%`;

/** Selling price per fee profile, priced by the server quote: the price splits into cost, the profile's fees and profit (dev-notes/PRICING.md). */
export function PricingSection({
  lines,
  additionalCosts,
  value,
  onChange,
  error,
}: PricingSectionProps) {
  const currency = useCurrency();
  const build = useBuildEstimate(lines, additionalCosts, value);
  const quote = build.quote;
  const cost = {
    amount: quote?.total?.amount ?? 0,
    partial: !!quote?.total?.partial,
  };
  const costKnown = !build.incomplete && !!quote?.total?.available;
  const rows = (quote?.profiles ?? []).map((profile) => ({
    profile,
    result: profile.pricing ?? { breakEven: 0, unreachable: false },
  }));
  // The server sends one unnamed "No fees" row while no fee profile exists.
  const noProfiles = rows.length === 0 || rows[0].profile.profileId == null;
  const unreachable = rows.filter((r) => r.result.unreachable);
  const loading = build.loading;

  // Switching mode keeps the top row's price: the toggle changes what is held fixed, not the price.
  const switchMode = (mode: PricingMode) => {
    const top = rows[0]?.result;
    if (mode === value.mode) return;
    if (!top || top.price == null || !costKnown) {
      onChange({ mode, value: '' });
      return;
    }
    const next =
      mode === 'Price'
        ? top.price.toFixed(2)
        : mode === 'Markup'
          ? top.markup == null
            ? ''
            : (top.markup * 100).toFixed(2)
          : top.margin == null || top.margin < 0
            ? ''
            : (top.margin * 100).toFixed(2);
    onChange({ mode, value: next === '' ? '' : String(Number(next)) });
  };

  return (
    <section
      aria-labelledby="pricing-heading"
      className="rounded-lg border bg-card"
    >
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 border-b px-4 py-3">
        <h3 id="pricing-heading" className="font-medium">
          Pricing
        </h3>
        <span className="text-sm text-muted-foreground tabular-nums">
          Cost{' '}
          {loading
            ? '…'
            : costKnown
              ? `${formatCost(cost.amount, currency)}${cost.partial ? ' (partial)' : ''}`
              : 'unavailable'}
        </span>
        <div className="flex flex-wrap items-center gap-2 sm:ml-auto">
          <ToggleGroup
            type="single"
            variant="outline"
            size="sm"
            aria-label="Set price by"
            value={value.mode}
            onValueChange={(next) => {
              if (next) switchMode(next as PricingMode);
            }}
          >
            {pricingModes.map((mode) => (
              <ToggleGroupItem key={mode} value={mode}>
                {mode}
              </ToggleGroupItem>
            ))}
          </ToggleGroup>
          {value.mode === 'Price' ? (
            <MoneyInput
              aria-label="Price"
              inputMode="decimal"
              placeholder="Not set"
              className="w-36"
              aria-invalid={!!error}
              value={value.value}
              onChange={(e) => onChange({ ...value, value: e.target.value })}
            />
          ) : (
            <InputGroup className="w-28">
              <InputGroupInput
                aria-label={value.mode}
                inputMode="decimal"
                placeholder="Not set"
                aria-invalid={!!error}
                value={value.value}
                onChange={(e) => onChange({ ...value, value: e.target.value })}
              />
              <InputGroupAddon align="inline-end">%</InputGroupAddon>
            </InputGroup>
          )}
        </div>
        {error && (
          <p role="alert" className="w-full text-sm text-destructive">
            {error}
          </p>
        )}
      </div>
      {loading ? (
        <Skeleton className="m-4 h-16 w-auto" />
      ) : (
        <div className="flex flex-col gap-3 px-2 pt-1 pb-4 sm:px-4">
          <Table aria-label="Profit by fee profile">
            <TableHeader>
              <TableRow>
                <TableHead scope="col">Fee profile</TableHead>
                <TableHead scope="col" className="text-right">
                  Price
                </TableHead>
                <TableHead
                  scope="col"
                  className="hidden text-right sm:table-cell"
                >
                  Fees
                </TableHead>
                <TableHead scope="col" className="text-right">
                  Profit
                </TableHead>
                <TableHead scope="col" className="text-right">
                  Margin
                </TableHead>
                <TableHead
                  scope="col"
                  className="hidden text-right md:table-cell"
                >
                  Markup
                </TableHead>
                <TableHead
                  scope="col"
                  className="hidden text-right sm:table-cell"
                >
                  Max discount
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map(({ profile, result }) => (
                <ProfileRows
                  key={profile.profileId ?? profile.name}
                  profile={profile}
                  result={result}
                  costKnown={costKnown}
                  cost={cost.amount}
                  currency={currency}
                />
              ))}
            </TableBody>
          </Table>
          <Legend />
          {(noProfiles ||
            unreachable.length > 0 ||
            (costKnown && cost.partial)) && (
            <ul className="flex flex-col gap-1 text-sm text-muted-foreground">
              {noProfiles && (
                <li>
                  No fee profiles.{' '}
                  <Link
                    to="/settings/fees"
                    className="underline underline-offset-4 hover:text-foreground"
                  >
                    Add in Settings → Fees
                  </Link>
                  .
                </li>
              )}
              {costKnown && cost.partial && (
                <li>Cost partial. Profit may be lower.</li>
              )}
              {unreachable.length > 0 && (
                <li>
                  Margin + {unreachable.map((r) => r.profile.name).join(', ')}{' '}
                  fees ≥ 100%.
                </li>
              )}
            </ul>
          )}
        </div>
      )}
    </section>
  );
}

function ProfileRows({
  profile,
  result,
  costKnown,
  cost,
  currency,
}: {
  profile: QuoteProfile;
  result: ProfilePricing;
  costKnown: boolean;
  cost: number;
  currency: string;
}) {
  const money = (n: number | null | undefined) =>
    n == null ? '—' : formatCost(n, currency);
  const known = (n: number | null | undefined) =>
    costKnown ? (n ?? null) : null;
  const loss = costKnown && result.profit != null && result.profit < 0;
  const discount = known(result.maxDiscount);
  const price = result.price;
  const breakdown =
    price == null || (profile.fees ?? []).length === 0
      ? undefined
      : (profile.fees ?? [])
          .map(
            (fee) =>
              `${fee.name} ${money(fee.type === 'Percent' ? (price * (fee.amount ?? 0)) / 100 : (fee.amount ?? 0))}`,
          )
          .join(', ');
  return (
    <>
      <TableRow className="border-b-0 hover:bg-transparent">
        <TableCell className="font-medium whitespace-normal">
          {profile.name}
        </TableCell>
        <TableCell className="text-right tabular-nums">
          {result.price != null ? (
            money(result.price)
          ) : costKnown && !result.unreachable ? (
            <span className="text-muted-foreground">
              Min {money(result.breakEven)}
            </span>
          ) : (
            '—'
          )}
        </TableCell>
        <TableCell
          className="hidden text-right tabular-nums sm:table-cell"
          title={breakdown}
        >
          {money(result.fees)}
        </TableCell>
        <TableCell
          className={cn(
            'text-right font-medium tabular-nums',
            loss && 'text-destructive',
          )}
        >
          {money(known(result.profit))}
        </TableCell>
        <TableCell
          className={cn('text-right tabular-nums', loss && 'text-destructive')}
        >
          {percent(known(result.margin))}
        </TableCell>
        <TableCell className="hidden text-right tabular-nums md:table-cell">
          {percent(known(result.markup), 0)}
        </TableCell>
        <TableCell className="hidden text-right tabular-nums sm:table-cell">
          {discount == null ? '—' : discount <= 0 ? 'None' : percent(discount)}
        </TableCell>
      </TableRow>
      <TableRow aria-hidden className="hover:bg-transparent">
        <TableCell colSpan={7} className="pt-0 pb-3">
          {costKnown && result.price != null && result.fees != null ? (
            <SplitBar
              cost={cost}
              fees={result.fees}
              profit={Math.max(0, result.profit ?? 0)}
              title={`Cost ${money(cost)}, fees ${money(result.fees)}, profit ${money(result.profit)}`}
            />
          ) : (
            <div className="h-1.5 rounded-full bg-muted" />
          )}
        </TableCell>
      </TableRow>
    </>
  );
}

/** Shares of the price; on a loss cost + fees fill the bar and no profit shows. 2px gaps keep fees and profit apart without colour. */
function SplitBar({
  cost,
  fees,
  profit,
  title,
}: {
  cost: number;
  fees: number;
  profit: number;
  title: string;
}) {
  const whole = cost + fees + profit;
  const segments = [
    { key: 'cost', amount: cost, className: 'bg-muted-foreground/40' },
    { key: 'fees', amount: fees, className: 'bg-chart-2' },
    { key: 'profit', amount: profit, className: 'bg-chart-3' },
  ].filter((s) => s.amount > 0);
  return (
    <div className="flex h-1.5 gap-0.5" title={title}>
      {segments.map((s) => (
        <div
          key={s.key}
          className={cn('rounded-full', s.className)}
          style={{ flexGrow: whole > 0 ? s.amount / whole : 0, flexBasis: 0 }}
        />
      ))}
    </div>
  );
}

function Legend() {
  return (
    <ul
      aria-hidden
      className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground"
    >
      {[
        ['Cost', 'bg-muted-foreground/40'],
        ['Fees', 'bg-chart-2'],
        ['Profit', 'bg-chart-3'],
      ].map(([label, swatch]) => (
        <li key={label} className="flex items-center gap-1.5">
          <span className={cn('size-2 rounded-full', swatch)} />
          {label}
        </li>
      ))}
    </ul>
  );
}
