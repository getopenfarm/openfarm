import type { PrintObject } from '@/lib/api/generated/endpoints.schemas';

/** Optional slicer outlines supplement the labelled keyboard-accessible controls. */
export function PrintObjectOutlines({
  objects,
  selected = [],
}: {
  objects: PrintObject[];
  selected?: string[];
}) {
  const outlines = objects.filter((o) => o.polygon && o.polygon.length >= 3);
  const points = outlines.flatMap((o) => o.polygon ?? []);
  if (points.length === 0) return null;
  const bounds = points.reduce(
    (b, p) => ({
      left: Math.min(b.left, p[0]),
      bottom: Math.min(b.bottom, p[1]),
      right: Math.max(b.right, p[0]),
      top: Math.max(b.top, p[1]),
    }),
    {
      left: Number.POSITIVE_INFINITY,
      bottom: Number.POSITIVE_INFINITY,
      right: Number.NEGATIVE_INFINITY,
      top: Number.NEGATIVE_INFINITY,
    },
  );
  const left = bounds.left - 2;
  const bottom = bounds.bottom - 2;
  const width = bounds.right - left + 2;
  const height = bounds.top - bottom + 2;
  return (
    <svg
      role="img"
      aria-label="Plate object outlines"
      viewBox={`${left} ${bottom} ${width} ${height}`}
      className="max-h-64 w-full rounded-md border bg-muted/20"
    >
      <title>Plate object outlines</title>
      {outlines.map((o) => (
        <polygon
          key={o.id}
          points={o.polygon?.map((p) => p.join(',')).join(' ')}
          transform={`translate(0 ${2 * bottom + height}) scale(1 -1)`}
          className={
            selected.includes(o.id ?? '')
              ? 'fill-primary/30 stroke-primary'
              : 'fill-muted stroke-muted-foreground'
          }
          strokeWidth={0.5}
        >
          <title>
            {o.name} · {o.id}
          </title>
        </polygon>
      ))}
    </svg>
  );
}
