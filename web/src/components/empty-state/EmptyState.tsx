import type { ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

interface EmptyStateProps {
  /** Optional icon element, e.g. <PrinterIcon className="size-8 text-muted-foreground" />. */
  icon?: ReactNode;
  /** Bold headline line. */
  title?: ReactNode;
  /** Muted supporting line. */
  description?: ReactNode;
  /** Optional action, e.g. a Button. */
  action?: ReactNode;
  /** 'sm' uses the tighter rounded-lg / p-8 frame for in-card placements. */
  size?: 'default' | 'sm';
  /** Whether to draw the dashed border frame. Off when nested inside another framed container. */
  bordered?: boolean;
  className?: string;
}

/**
 * Centered empty / placeholder state: the dashed-border block repeated across
 * list and detail pages.
 */
export function EmptyState({
  icon,
  title,
  description,
  action,
  size = 'default',
  bordered = true,
  className,
}: EmptyStateProps) {
  return (
    <div
      data-slot="empty-state"
      className={cn(
        'flex flex-col items-center gap-2 text-center',
        bordered && 'border border-dashed',
        size === 'sm' ? 'rounded-lg p-8' : 'rounded-xl p-12',
        className,
      )}
    >
      {icon}
      {title ? <p className="text-sm font-medium">{title}</p> : null}
      {description ? (
        <p className="text-sm text-muted-foreground">{description}</p>
      ) : null}
      {action}
    </div>
  );
}

interface ErrorStateProps {
  icon?: ReactNode;
  title?: ReactNode;
  description?: ReactNode;
  onRetry: () => void;
  retryLabel?: string;
  size?: 'default' | 'sm';
  bordered?: boolean;
  className?: string;
}

/** EmptyState preset for load failures, with a Retry button wired to onRetry. */
export function ErrorState({
  onRetry,
  retryLabel = 'Retry',
  ...props
}: ErrorStateProps) {
  return (
    <EmptyState
      {...props}
      action={
        <Button variant="outline" onClick={onRetry}>
          {retryLabel}
        </Button>
      }
    />
  );
}
