import { cn } from '@/lib/utils';

/** Remaining filament as a bar: spool colour for what is left, hatch for what is used (as on the dashboard rail). */
export function FillMeter({
  colorHex,
  percent,
  className,
}: {
  colorHex?: string;
  percent: number;
  className?: string;
}) {
  const level = Math.min(100, Math.max(0, percent));
  return (
    <span
      aria-hidden="true"
      className={cn(
        'filament-consumed relative block h-1.5 overflow-hidden rounded-sm',
        className,
      )}
    >
      <span
        className="absolute inset-y-0 left-0"
        style={{
          width: `${level}%`,
          backgroundColor: colorHex ?? 'var(--muted-foreground)',
        }}
      />
    </span>
  );
}
