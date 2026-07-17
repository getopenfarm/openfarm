import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

interface PageHeaderProps {
  /** Main heading text. */
  title: ReactNode;
  /** Muted supporting line under the title. Accepts rich nodes (e.g. counts). */
  description?: ReactNode;
  /** Trailing action, typically a Button or a dialog trigger. */
  action?: ReactNode;
  /** Content rendered above the title, e.g. a BackLink. */
  above?: ReactNode;
  className?: string;
}

/**
 * Standard page header: an optional above-slot, a title, an optional muted
 * description, and an optional trailing action — the block every list page
 * repeated by hand.
 */
export function PageHeader({
  title,
  description,
  action,
  above,
  className,
}: PageHeaderProps) {
  return (
    <div
      data-slot="page-header"
      className={cn(
        'flex flex-wrap items-center justify-between gap-4',
        className,
      )}
    >
      <div>
        {above}
        <h2 className="font-heading text-2xl font-semibold">{title}</h2>
        {description ? (
          <p className="text-sm text-muted-foreground">{description}</p>
        ) : null}
      </div>
      {action}
    </div>
  );
}
