import type { ComponentProps } from 'react';
import { cn } from '@/lib/utils';

/**
 * Page root: owns the vertical rhythm between the PageHeader and the content
 * below it.
 */
export function Page({ className, ...props }: ComponentProps<'div'>) {
  return (
    <div
      data-slot="page"
      className={cn('flex flex-col gap-6', className)}
      {...props}
    />
  );
}
