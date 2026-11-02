import { FolderIcon, ImageOffIcon, Layers3Icon } from 'lucide-react';
import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

type PreviewKind = 'folder' | 'assembly' | 'part';

/** Every library tile has the same fixed height; extra fields shrink the preview instead. */
export const LIBRARY_TILE_CLASS =
  'h-52 transition-colors group-hover/library-tile:bg-muted/50 group-focus-within/library-tile:bg-muted/50';

/**
 * Up to four images. Cards show them as a 1×1 or 2×2 collage; compact rows show the
 * first as a cover with a "+N" badge.
 */
export function LibraryPreview({
  images = [],
  kind,
  compact = false,
  fill = false,
}: {
  images?: string[];
  kind: PreviewKind;
  compact?: boolean;
  fill?: boolean;
}) {
  const previews = Array.from(new Set(images)).slice(0, 4);
  const Icon =
    kind === 'folder'
      ? FolderIcon
      : kind === 'assembly'
        ? Layers3Icon
        : ImageOffIcon;
  return (
    <span
      aria-hidden="true"
      className={cn(
        'relative flex shrink-0 items-center justify-center overflow-hidden rounded-md bg-muted text-muted-foreground',
        // fill sits out of flow so a collage can never set its container's height.
        compact ? 'size-12' : fill ? 'absolute inset-0' : 'size-full',
      )}
    >
      {previews.length ? (
        <span
          className={cn(
            'flex size-full',
            compact ? 'p-0.5' : 'gap-1 p-2',
            !compact &&
              previews.length > 1 &&
              'grid auto-rows-[minmax(0,1fr)] grid-cols-2',
          )}
        >
          {(compact ? previews.slice(0, 1) : previews).map((src) => (
            <PreviewImage key={src} src={src} />
          ))}
          {kind === 'folder' && (
            // A folder collage would otherwise read as an assembly.
            <Badge compact={compact} className="left-0.5 text-muted-foreground">
              <FolderIcon className={compact ? 'size-3' : 'size-4'} />
            </Badge>
          )}
          {compact && previews.length > 1 && (
            <Badge
              compact
              className="right-0.5 px-1 text-[10px] font-medium text-foreground"
            >
              +{previews.length - 1}
            </Badge>
          )}
        </span>
      ) : (
        <Icon className={cn(compact ? 'size-5' : 'size-12')} />
      )}
    </span>
  );
}

function PreviewImage({ src }: { src: string }) {
  return (
    <img
      src={`data:image/png;base64,${src}`}
      alt=""
      loading="lazy"
      draggable={false}
      className="size-full min-h-0 min-w-0 object-contain"
    />
  );
}

function Badge({
  compact,
  className,
  children,
}: {
  compact: boolean;
  className?: string;
  children: ReactNode;
}) {
  return (
    <span
      className={cn(
        'absolute bottom-0.5 flex items-center justify-center rounded-sm bg-background/90',
        compact ? 'p-px' : 'p-1',
        className,
      )}
    >
      {children}
    </span>
  );
}
