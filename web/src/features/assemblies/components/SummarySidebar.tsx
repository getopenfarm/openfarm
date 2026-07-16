import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { formatMinutes } from '@/lib/formatMinutes';
import type { Assembly, AssemblyComponent } from '../data/mockAssembliesStore';
import { computeAssemblyTotals } from '../lib/computeAssemblyTotals';

interface SummarySidebarProps {
  components: AssemblyComponent[];
  allAssemblies: Assembly[];
}

/** Sticky live summary for the assembly form. Computed over the in-progress
 * draft's component list (not the committed store) via the same
 * `computeAssemblyTotals` the list page and store use — never forks the
 * math. */
export function SummarySidebar({
  components,
  allAssemblies,
}: SummarySidebarProps) {
  // `computeAssemblyTotals` only reads `assembly.components`, so a draft
  // shape with placeholder id/name/description is safe to pass through.
  const draftAssembly: Assembly = {
    id: '__draft__',
    name: '',
    description: '',
    components,
  };
  const totals = computeAssemblyTotals(draftAssembly, allAssemblies);
  const totalQuantity = components.reduce(
    (sum, component) => sum + component.quantity,
    0,
  );

  return (
    <Card className="sticky top-6 h-fit">
      <CardHeader>
        <CardTitle>Summary</CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <span className="text-sm text-muted-foreground">Parts</span>
          <span className="font-mono text-sm">{totals.totalParts}</span>
        </div>
        <div className="flex items-center justify-between">
          <span className="text-sm text-muted-foreground">Total qty</span>
          <span className="font-mono text-sm">{totalQuantity}</span>
        </div>
        <div className="flex items-center justify-between">
          <span className="text-sm text-muted-foreground">Est. print time</span>
          <span className="font-mono text-sm">
            {formatMinutes(totals.estimatedTimeMinutes)}
          </span>
        </div>
      </CardContent>
    </Card>
  );
}
