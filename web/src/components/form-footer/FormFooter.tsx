import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

interface FormFooterProps {
  start?: ReactNode;
  end: ReactNode;
  className?: string;
}

export function FormFooter({ start, end, className }: FormFooterProps) {
  return (
    <div
      data-slot="form-footer"
      className={cn(
        'sticky -bottom-4 -mx-4 flex items-center gap-2 border-t bg-background/80 px-4 py-3 backdrop-blur-sm sm:-mx-6 sm:px-6',
        className,
      )}
    >
      {start}
      <div className="ml-auto flex items-center gap-2">{end}</div>
    </div>
  );
}
