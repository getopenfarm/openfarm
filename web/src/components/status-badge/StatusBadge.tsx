import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import type { BadgeKind } from './kinds';

interface StatusBadgeProps {
  kind: BadgeKind;
  label: string;
  /** Filled chip for a state the operator must act on. Amber and red only. */
  solid?: boolean;
  /** Makes the chip a button. `label` is its accessible name. The chip is text only: no dot, no icon. */
  action?: {
    onClick: () => void;
    label: string;
    disabled?: boolean;
  };
  className?: string;
}

export function StatusBadge({
  kind,
  label,
  solid,
  action,
  className,
}: StatusBadgeProps) {
  if (!action)
    return (
      <Badge
        kind={kind}
        solid={solid}
        data-slot="status-badge"
        className={className}
      >
        {label}
      </Badge>
    );
  return (
    <Badge
      asChild
      kind={kind}
      solid={solid}
      className={cn(
        'cursor-pointer hover:brightness-90 disabled:cursor-wait disabled:opacity-50',
        className,
      )}
    >
      <button
        type="button"
        data-slot="status-badge"
        aria-label={action.label}
        disabled={action.disabled}
        onClick={action.onClick}
      >
        {label}
      </button>
    </Badge>
  );
}
