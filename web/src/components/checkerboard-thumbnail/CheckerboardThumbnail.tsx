import type { LucideIcon } from 'lucide-react';
import { cn } from '@/lib/utils';

interface CheckerboardThumbnailProps {
  icon?: LucideIcon;
  label?: string;
  className?: string;
}

export function CheckerboardThumbnail({
  icon: Icon,
  label,
  className,
}: CheckerboardThumbnailProps) {
  return (
    <div
      data-slot="checkerboard-thumbnail"
      className={cn(
        'checkerboard relative flex shrink-0 items-center justify-center overflow-hidden rounded-md',
        className,
      )}
    >
      {Icon ? <Icon className="size-5 text-muted-foreground/50" /> : null}
      {label ? (
        <span className="absolute right-1 bottom-1 rounded bg-background/80 px-1 font-mono text-[10px] font-medium text-muted-foreground">
          {label}
        </span>
      ) : null}
    </div>
  );
}
