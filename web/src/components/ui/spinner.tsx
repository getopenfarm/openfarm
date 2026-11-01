import { Loader2Icon } from 'lucide-react';
import { cn } from '@/lib/utils';

function Spinner({ className, ...props }: React.ComponentProps<'svg'>) {
  // Inline indicators accompany an already named action; standalone indicators
  // still announce loading. Callers can override these defaults explicitly.
  const inline = 'data-icon' in props;
  return (
    <Loader2Icon
      data-slot="spinner"
      role={inline ? undefined : 'status'}
      aria-label={inline ? undefined : 'Loading'}
      aria-hidden={inline || undefined}
      className={cn('size-4 animate-spin', className)}
      {...props}
    />
  );
}

export { Spinner };
