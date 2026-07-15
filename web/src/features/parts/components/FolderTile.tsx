import { FolderIcon } from 'lucide-react';

interface FolderTileProps {
  name: string;
  count: number;
  onClick: () => void;
}

export function FolderTile({ name, count, onClick }: FolderTileProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="flex flex-col items-start gap-3 rounded-xl border p-4 text-left transition-colors hover:bg-muted/40"
    >
      <div className="flex size-10 items-center justify-center rounded-md bg-kind-blue/10 text-kind-blue">
        <FolderIcon className="size-5" />
      </div>
      <div className="flex flex-col gap-0.5">
        <span className="font-medium">{name}</span>
        <span className="text-xs text-muted-foreground">
          {count} {count === 1 ? 'part' : 'parts'}
        </span>
      </div>
    </button>
  );
}
