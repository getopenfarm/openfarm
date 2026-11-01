import type { ColumnDef } from '@tanstack/react-table';
import { ChevronDownIcon } from 'lucide-react';
import { useMemo } from 'react';
import { DataTable } from '@/components/data-table/DataTable';
import { ErrorState } from '@/components/empty-state/EmptyState';
import { Button } from '@/components/ui/button';
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible';
import { Skeleton } from '@/components/ui/skeleton';
import { pluralize } from '@/lib/pluralize';
import type { PlanPart } from './productionPlanner';
import type { RunLineEstimate } from './useRunEstimate';

/** Requirements are already flattened and aggregated by the server's BOM planner. */
export function RunPlanPreview({
  estimate,
  sets,
  name,
}: {
  estimate?: RunLineEstimate;
  sets?: number;
  name: string;
}) {
  const columns = useMemo<ColumnDef<PlanPart>[]>(
    () => [
      { accessorKey: 'name', header: 'Part', enableSorting: false },
      ...(sets
        ? [
            {
              id: 'perSet',
              header: 'Units per set',
              cell: ({ row }: { row: { original: PlanPart } }) =>
                row.original.requiredUnits / sets,
              enableSorting: false,
            },
          ]
        : []),
      {
        accessorKey: 'requiredUnits',
        header: 'Required units',
        enableSorting: false,
      },
      {
        accessorKey: 'unitsPerPlate',
        header: 'Units per plate',
        enableSorting: false,
      },
      { accessorKey: 'plates', header: 'Plates', enableSorting: false },
    ],
    [sets],
  );

  if (!estimate?.valid)
    return (
      <p className="text-sm text-muted-foreground">
        Enter a whole quantity of 1 or more to preview production.
      </p>
    );
  if (estimate.isPending)
    return (
      <div role="status" aria-label={`Planning ${name}`}>
        <Skeleton className="h-20 w-full" />
      </div>
    );
  if (estimate.isError)
    return (
      <ErrorState
        size="sm"
        bordered={false}
        title={`Could not plan ${name}.`}
        description={
          estimate.error instanceof Error ? estimate.error.message : undefined
        }
        onRetry={() => void estimate.refetch()}
      />
    );
  const plan = estimate.plan;
  if (!plan) return null;
  const partial = plan.parts.some((part) =>
    part.jobUnits?.some((units) => units < part.unitsPerPlate),
  );
  return (
    <div className="flex min-w-0 flex-col gap-3">
      <p className="text-sm text-muted-foreground tabular-nums">
        {pluralize(plan.plates, 'plate')} ·{' '}
        {pluralize(
          plan.parts.reduce((sum, part) => sum + part.plannedUnits, 0),
          'printed component unit',
        )}
        {plan.completeSets != null &&
          ` · ${pluralize(plan.completeSets, 'complete set')}`}
      </p>
      {sets != null && (
        <section aria-label={`Components for ${name}`}>
          <DataTable
            columns={columns}
            data={plan.parts}
            getRowId={(part) => part.partId}
          />
        </section>
      )}
      <Collapsible>
        <CollapsibleTrigger asChild>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            aria-label={`Plate details for ${name}`}
          >
            <ChevronDownIcon data-icon="inline-start" />
            Plate details
          </Button>
        </CollapsibleTrigger>
        <CollapsibleContent className="pt-2">
          <ul className="flex flex-col gap-2 text-sm">
            {plan.parts.map((part) => (
              <li key={part.partId}>
                <span className="font-medium">{part.name}:</span>{' '}
                {part.jobUnits?.join(' + ') ?? part.plannedUnits} units across{' '}
                {pluralize(part.plates, 'plate')}
              </li>
            ))}
          </ul>
          {partial && (
            <p className="mt-2 text-sm text-muted-foreground">
              The final plate uses fewer objects to meet the exact target. Time
              and filament are estimated proportionally; startup, purge, and
              travel may not scale linearly.
            </p>
          )}
        </CollapsibleContent>
      </Collapsible>
    </div>
  );
}
