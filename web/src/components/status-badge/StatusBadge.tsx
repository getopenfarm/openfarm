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
    <span
      data-slot="status-badge"
      className={cn(
        'inline-flex h-5 w-fit shrink-0 items-center gap-1.5 rounded-full px-2 text-xs font-medium whitespace-nowrap',
        meta.bgClass,
        meta.textClass,
        className,
      )}
    >
      <span className={cn('size-1.5 shrink-0 rounded-full', meta.dotClass)} />
      {label}
    </span>
  );
}
