import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { pluralize } from '@/lib/pluralize';
import { formatEstimateMetric } from '@/lib/printEstimates';
import type { ProductionPlan } from './productionPlanner';

export function PlanSummary({ plan }: { plan: ProductionPlan }) {
  return (
    <div className="flex flex-col gap-3" aria-live="polite">
      <p className="text-sm text-muted-foreground tabular-nums">
        {pluralize(plan.plates, 'plate')} ·{' '}
        {plan.parts.reduce((sum, p) => sum + p.plannedUnits, 0)} planned units
        {plan.completeSets != null &&
          ` · ${pluralize(plan.completeSets, 'complete set')}`}
        {` · ${formatEstimateMetric(plan.estimate?.time, 'time')} · ${formatEstimateMetric(plan.estimate?.filament, 'filament')}`}
      </p>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Part</TableHead>
            <TableHead>Required units</TableHead>
            <TableHead>Plates</TableHead>
            <TableHead>Planned units</TableHead>
            <TableHead>Units per job</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {plan.parts.map((part) => (
            <TableRow key={part.partId}>
              <TableCell>{part.name}</TableCell>
              <TableCell>{part.requiredUnits}</TableCell>
              <TableCell>{part.plates}</TableCell>
              <TableCell>{part.plannedUnits}</TableCell>
              <TableCell>
                {part.jobUnits?.join(' + ') ?? part.plannedUnits}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
