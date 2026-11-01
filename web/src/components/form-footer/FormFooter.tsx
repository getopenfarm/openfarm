import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

interface FormFooterProps {
  end: ReactNode;
  className?: string;
}

export function FormFooter({ end, className }: FormFooterProps) {
  return (
    <div
      data-slot="form-footer"
      className={cn(
        'sticky bottom-0 -mx-4 mt-auto -mb-4 flex flex-wrap items-center gap-2 border-t bg-background/80 px-4 pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))] backdrop-blur-sm',
        className,
      )}
    >
      <div className="ml-auto flex max-w-full flex-wrap items-center justify-end gap-2">
        {end}
      </div>
    </div>
  );
}
