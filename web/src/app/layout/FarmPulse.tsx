import { STATUS_META } from '@/components/printer-tile/status';
import { Badge } from '@/components/ui/badge';

// TODO(Phase 1 follow-up): replace sampleCounts with a live summary derived from
// the fleet API / SignalR farm hub once the dashboard consumes GetFleetQuery.
const sampleCounts: Array<{ status: keyof typeof STATUS_META; count: number }> =
  [
    { status: 'Printing', count: 4 },
    { status: 'AwaitingClear', count: 1 },
    { status: 'Idle', count: 2 },
  ];

export function FarmPulse() {
  return (
    <div className="hidden items-center gap-1.5 sm:flex">
      {sampleCounts.map(({ status, count }) => {
        const meta = STATUS_META[status];
        return (
          <Badge key={status} variant="outline" className="gap-1.5">
            <span className={`size-1.5 rounded-full ${meta.dotClass}`} />
            <span className="text-muted-foreground">
              {count} {meta.label.toLowerCase()}
            </span>
          </Badge>
        );
      })}
    </div>
  );
}
