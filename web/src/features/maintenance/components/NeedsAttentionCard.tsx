import { toast } from 'sonner';
import { StatusBadge } from '@/components/status-badge/StatusBadge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { cn } from '@/lib/utils';
import type { MatchedMaintenanceFlag } from '../lib/matchMaintenanceToFleet';

interface NeedsAttentionCardProps {
  flag: MatchedMaintenanceFlag;
}

export function NeedsAttentionCard({ flag }: NeedsAttentionCardProps) {
  const isOverdue = flag.severity === 'overdue';

  return (
    <Card
      className={cn(
        'border',
        isOverdue
          ? 'border-kind-red/30 bg-kind-red/5'
          : 'border-kind-amber/30 bg-kind-amber/5',
      )}
    >
      <CardContent className="flex flex-col gap-3">
        <div className="flex items-start justify-between gap-2">
          <div>
            <p className="font-heading text-sm font-semibold">
              {flag.printerName}
            </p>
            {flag.printerModel && (
              <p className="font-mono text-xs text-muted-foreground">
                {flag.printerModel}
              </p>
            )}
          </div>
          <StatusBadge
            kind={isOverdue ? 'red' : 'amber'}
            label={isOverdue ? 'Overdue' : 'Due soon'}
          />
        </div>
        <p className="text-sm">
          <span className="font-medium">{flag.task}</span>
          <span className="text-muted-foreground"> — {flag.detailText}</span>
        </p>
        <Button
          variant="outline"
          className="w-full"
          onClick={() => toast('Not implemented.')}
        >
          Log maintenance
        </Button>
      </CardContent>
    </Card>
  );
}
