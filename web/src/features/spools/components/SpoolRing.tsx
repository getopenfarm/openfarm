import { useId } from 'react';
import { cn } from '@/lib/utils';

const RADIUS = 20;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

/**
 * A spool seen end-on. The ring is the filament in its own color: solid for what is left, filled clockwise from the top,
 * hatched for what is used (the dashboard rail's convention). With no color yet it is all hatch, like unassigned filament there.
 */
export function SpoolRing({
  colorHex,
  fraction = 1,
  className,
}: {
  colorHex?: string;
  /** What is left, 0 to 1. */
  fraction?: number;
  className?: string;
}) {
  const hatchId = useId();
  const level = Math.min(1, Math.max(0, fraction));
  return (
    <svg
      viewBox="0 0 56 56"
      aria-hidden="true"
      data-slot="spool-ring"
      className={cn('size-14 shrink-0', className)}
    >
      <defs>
        <pattern
          id={hatchId}
          width="3"
          height="3"
          patternUnits="userSpaceOnUse"
          patternTransform="rotate(45)"
        >
          <rect width="3" height="3" className="fill-muted" />
          <line
            x1="0"
            y1="0"
            x2="0"
            y2="3"
            strokeWidth="1"
            className="stroke-border"
          />
        </pattern>
      </defs>
      <circle
        cx="28"
        cy="28"
        r={RADIUS}
        fill="none"
        strokeWidth="10"
        stroke={`url(#${hatchId})`}
      />
      {colorHex && (
        <circle
          cx="28"
          cy="28"
          r={RADIUS}
          fill="none"
          strokeWidth="10"
          stroke={colorHex}
          strokeDasharray={`${level * CIRCUMFERENCE} ${CIRCUMFERENCE}`}
          transform="rotate(-90 28 28)"
          data-slot="spool-ring-fill"
          className="transition-[stroke-dasharray] duration-200 motion-reduce:transition-none"
        />
      )}
      {/* Edges drawn from the text color, so a white filament on a light card or a black one on a dark card still reads as a spool. */}
      <circle
        cx="28"
        cy="28"
        r="25.5"
        fill="none"
        className="stroke-foreground/25"
      />
      <circle
        cx="28"
        cy="28"
        r="14.5"
        fill="none"
        className="stroke-foreground/25"
      />
      <circle
        cx="28"
        cy="28"
        r="5.5"
        className="fill-card stroke-foreground/25"
      />
    </svg>
  );
}
