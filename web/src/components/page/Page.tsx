import type { ComponentProps } from 'react';
import { cn } from '@/lib/utils';

/**
 * Page root: owns the vertical rhythm between the PageHeader and the content
 * below it, plus bottom clearance for the floating table selection bar.
 */
export function Page({ className, ...props }: ComponentProps<'div'>) {
  return (
    <div
      data-slot="page"
      className={cn(
        'flex flex-col gap-6 has-[[data-slot=table-selection-bar]]:pb-[calc(8rem+env(safe-area-inset-bottom))]',
        className,
      )}
      {...props}
    />
  );
}
