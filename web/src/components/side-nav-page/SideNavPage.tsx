import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

/** Class for one side-nav entry (a NavLink or a tab button). */
export function sideNavItemClass(active: boolean) {
  return cn(
    'shrink-0 whitespace-nowrap rounded-lg px-3 py-[7px] text-left text-[13px] md:px-[11px] md:py-2 md:text-[13.5px] font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground',
    active && 'bg-accent text-foreground',
  );
}

interface SideNavPageProps {
  title: ReactNode;
  description: ReactNode;
  /** Accessible name of the nav. */
  navLabel: string;
  nav: ReactNode;
  children: ReactNode;
}

/** Full-bleed page: header rule, 200px side nav, scrolling content. */
export function SideNavPage({
  title,
  description,
  navLabel,
  nav,
  children,
}: SideNavPageProps) {
  return (
    // -m-4 cancels AppShell's padding so the header rule and nav divider reach the edges.
    <div className="-m-4 flex flex-1 flex-col">
      <header className="border-b px-7 pt-5 pb-4">
        <h1 className="font-heading text-[18px] leading-tight md:text-[22px] font-semibold tracking-[-0.02em]">
          {title}
        </h1>
        <p className="mt-[3px] hidden text-[13px] text-muted-foreground md:block">
          {description}
        </p>
      </header>
      <div className="flex min-h-0 flex-1 flex-col md:flex-row">
        <nav
          aria-label={navLabel}
          className="flex shrink-0 gap-0.5 overflow-x-auto border-b px-4 py-2 md:w-[200px] md:gap-[3px] md:px-3.5 md:py-[18px] md:flex-col md:border-r md:border-b-0"
        >
          {nav}
        </nav>
        <div className="flex min-w-0 flex-1 flex-col overflow-y-auto px-4 pt-4 pb-8 md:px-7 md:pt-6 md:pb-12">
          {children}
        </div>
      </div>
    </div>
  );
}
