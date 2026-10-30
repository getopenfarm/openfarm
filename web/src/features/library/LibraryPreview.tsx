import { FolderIcon, ImageOffIcon, Layers3Icon } from 'lucide-react';
import { cn } from '@/lib/utils';

/** A single preview or up to four images, shared by compact rows and thumbnail cards. */
export function LibraryPreview({
  images = [],
  kind,
  compact = false,
}: {
  images?: string[];
  kind: 'folder' | 'assembly' | 'part';
  compact?: boolean;
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
        'flex shrink-0 items-center justify-center overflow-hidden rounded-md bg-muted text-muted-foreground',
        compact ? 'size-10' : 'size-full',
      )}
    >
      {previews.length ? (
        <span
          className={cn(
            'flex size-full',
            compact ? 'gap-px p-0.5' : 'gap-1 p-2',
            previews.length > 1 && 'grid grid-cols-2',
          )}
        >
          {previews.map((src) => (
            <img
              key={src}
              src={`data:image/png;base64,${src}`}
              alt=""
              loading="lazy"
              className="size-full min-h-0 min-w-0 object-contain"
            />
          ))}
        </span>
      ) : (
        <Icon className={cn(compact ? 'size-5' : 'size-12')} />
      )}
    </span>
  );
}
