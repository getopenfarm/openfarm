import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';

interface SkeletonGridProps {
  /** Number of placeholder cards. */
  count?: number;
  /** Grid container classes (overrides the default responsive columns). */
  className?: string;
  /** Per-card classes (overrides the default height). */
  itemClassName?: string;
}

/** Responsive grid of skeleton placeholder cards used while a list loads. */
export function SkeletonGrid({
  count = 3,
  className,
  itemClassName,
}: SkeletonGridProps) {
  return (
    <div
      className={cn(
        'grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3',
        className,
      )}
    >
      {Array.from({ length: count }).map((_, index) => (
        <Skeleton
          // biome-ignore lint/suspicious/noArrayIndexKey: static skeleton placeholder cards
          key={`skeleton-${index}`}
          className={cn('h-40 w-full', itemClassName)}
        />
      ))}
    </div>
  );
}
