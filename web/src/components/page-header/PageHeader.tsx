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
  /** `section` is a smaller heading for a pane inside a page, e.g. a Settings section. */
  size?: 'page' | 'section';
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
  size = 'page',
  className,
}: PageHeaderProps) {
  return (
    <div
      data-slot="page-header"
      className={cn(
        'flex shrink-0 flex-wrap items-center justify-between gap-4',
        className,
      )}
    >
      <div>
        {above}
        <h2
          className={cn(
            'font-heading font-semibold',
            size === 'page' ? 'text-[22px] tracking-[-0.02em]' : 'text-base',
          )}
        >
          {title}
        </h2>
        {description ? (
          <p className="text-[13px] text-muted-foreground">{description}</p>
        ) : null}
      </div>
      {action}
    </div>
  );
}
