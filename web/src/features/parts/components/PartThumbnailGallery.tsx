import { ImageOffIcon } from 'lucide-react';
import type { ThumbnailResponse } from '@/lib/api/generated/endpoints.schemas';
import { cn } from '@/lib/utils';

/** "Plate 4 · Clip": a plate by its number and, when the slicer named it, its name. */
export function plateLabel(plate: { number?: number; name?: string | null }) {
  return `Plate ${plate.number ?? 1}${plate.name ? ` · ${plate.name}` : ''}`;
}

interface PartThumbnailGalleryProps {
  /** One per plate, indexed by plate number - 1. */
  thumbnails: ThumbnailResponse[] | undefined;
  className?: string;
  /** Per-tile size in Tailwind arbitrary-value form, e.g. "5rem". Defaults to a compact strip size. */
  tileSize?: string;
}

/** Renders {thumbnailBase64} as a data: URI directly — no fetch of its own (research.md §8). */
export function PartThumbnailGallery({
  thumbnails,
  className,
  tileSize = '5rem',
}: PartThumbnailGalleryProps) {
  const sorted = [...(thumbnails ?? [])].sort(
    (a, b) => (a.plateIndex ?? 0) - (b.plateIndex ?? 0),
  );

  if (sorted.length === 0) {
    return (
      <div
        className={cn(
          'flex items-center justify-center rounded-lg border border-dashed text-muted-foreground',
          className,
        )}
        style={{ width: tileSize, height: tileSize }}
      >
        <ImageOffIcon className="size-5" />
      </div>
    );
  }

  return (
    <div className={cn('flex flex-wrap gap-2', className)}>
      {sorted.map((thumbnail) => {
        const plateIndex = thumbnail.plateIndex ?? 0;
        return (
          <div
            key={plateIndex}
            className="relative overflow-hidden rounded-lg border bg-muted/40"
            style={{ width: tileSize, height: tileSize }}
            title={`Plate ${plateIndex + 1}`}
          >
            {thumbnail?.thumbnailBase64 ? (
              <img
                src={`data:image/png;base64,${thumbnail.thumbnailBase64}`}
                alt={`Plate ${plateIndex + 1} preview`}
                className="size-full object-contain"
              />
            ) : (
              <div className="flex size-full items-center justify-center text-muted-foreground">
                <ImageOffIcon className="size-4" />
              </div>
            )}
            {sorted.length > 1 ? (
              <span className="absolute bottom-0.5 right-1 text-[10px] font-medium text-muted-foreground">
                {plateIndex + 1}
              </span>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}
