import { ImageOffIcon } from 'lucide-react';
import type { ThumbnailResponse } from '@/lib/api/generated/endpoints.schemas';
import { cn } from '@/lib/utils';

interface PartThumbnailGalleryProps {
  thumbnails: ThumbnailResponse[] | undefined;
  /** Total number of plates the file has, independent of how many produced a thumbnail
   * (research.md §8) — lets a plate with no derivable preview render as an explicit
   * "no thumbnail" placeholder rather than looking like a missing plate. */
  plateCount: number | undefined;
  className?: string;
  /** Per-tile size in Tailwind arbitrary-value form, e.g. "5rem". Defaults to a compact strip size. */
  tileSize?: string;
}

/** Renders {thumbnailBase64} as a data: URI directly — no fetch of its own (research.md §8). */
export function PartThumbnailGallery({
  thumbnails,
  plateCount,
  className,
  tileSize = '5rem',
}: PartThumbnailGalleryProps) {
  const total = plateCount ?? thumbnails?.length ?? 0;

  if (total === 0) {
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

  const byPlateIndex = new Map(
    (thumbnails ?? []).map((thumbnail) => [thumbnail.plateIndex, thumbnail]),
  );

  return (
    <div className={cn('flex flex-wrap gap-2', className)}>
      {Array.from({ length: total }, (_, plateIndex) => {
        const thumbnail = byPlateIndex.get(plateIndex);
        return (
          <div
            // biome-ignore lint/suspicious/noArrayIndexKey: plateIndex is the stable identity here
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
            {total > 1 ? (
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
