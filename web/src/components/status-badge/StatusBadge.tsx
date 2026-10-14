import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import { BADGE_KIND_META, type BadgeKind } from './kinds';

interface StatusBadgeProps {
  kind: BadgeKind;
  label: string;
  className?: string;
}

export function StatusBadge({ kind, label, className }: StatusBadgeProps) {
  const meta = BADGE_KIND_META[kind];
  return (
    <Badge
      kind={kind}
      data-slot="status-badge"
      className={cn('gap-1.5', className)}
    >
      <span className={cn('size-1.5 shrink-0 rounded-full', meta.dotClass)} />
      {label}
    </Badge>
  );
}
