import type { ReactNode } from 'react';
import { Card } from '@/components/ui/card';
import { cn } from '@/lib/utils';

interface KpiCardProps {
  label: string;
  value: ReactNode;
  sub?: ReactNode;
  dotClassName?: string;
  className?: string;
}

export function KpiCard({
  label,
  value,
  sub,
  dotClassName,
  className,
}: KpiCardProps) {
  return (
    <Card className={cn('px-4.5 py-4', className)}>
      <div className="flex items-center gap-2">
        <span
          className={cn(
            'size-2 shrink-0 rounded-full',
            dotClassName ?? 'bg-muted-foreground',
          )}
        />
        <span className="text-sm text-muted-foreground">{label}</span>
      </div>
      <div className="mt-2.5 font-heading text-[1.9rem] leading-none font-semibold tracking-tight">
        {value}
      </div>
      {sub ? (
        <div className="mt-1.5 text-xs text-muted-foreground/70">{sub}</div>
      ) : null}
    </Card>
  );
}
