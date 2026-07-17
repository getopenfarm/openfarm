import { ArrowLeftIcon } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { cn } from '@/lib/utils';

interface BackLinkProps {
  /** Route to navigate to. Renders an anchor. Provide either `to` or `onClick`. */
  to?: string;
  /** Click handler. Renders a button (for in-page back navigation). */
  onClick?: () => void;
  children: ReactNode;
  className?: string;
}

/**
 * Muted "← Back to X" navigation link. Renders a router Link when given `to`,
 * or a button when given `onClick`.
 */
export function BackLink({ to, onClick, children, className }: BackLinkProps) {
  const classes = cn(
    'inline-flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground',
    className,
  );
  const content = (
    <>
      <ArrowLeftIcon className="size-4" /> {children}
    </>
  );

  if (to) {
    return (
      <Link to={to} className={classes}>
        {content}
      </Link>
    );
  }

  return (
    <button type="button" onClick={onClick} className={classes}>
      {content}
    </button>
  );
}
