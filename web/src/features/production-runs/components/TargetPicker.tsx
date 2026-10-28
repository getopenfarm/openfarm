import { ComponentIcon, ImageOffIcon, PlusIcon } from 'lucide-react';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { useBomCatalog } from '@/features/assemblies/components/useBomCatalog';
import type { RunTargetKind } from './useRunEstimate';

export interface RunTarget {
  kind: RunTargetKind;
  id: string;
  name: string;
}

/** Thumbnail of a part, or the assembly glyph; 2.5rem, so a list of targets scans by shape. */
export function TargetThumbnail({
  kind,
  thumbnailBase64,
}: {
  kind: RunTargetKind;
  thumbnailBase64?: string | null;
}) {
  return (
    <div className="flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-md bg-muted text-muted-foreground">
      {kind === 'Assembly' ? (
        <ComponentIcon className="size-4" />
      ) : thumbnailBase64 ? (
        <img
          src={`data:image/png;base64,${thumbnailBase64}`}
          alt=""
          className="size-full object-contain"
        />
      ) : (
        <ImageOffIcon className="size-4" />
      )}
    </div>
  );
}

/** Search parts and assemblies together; a part that cannot be queued yet says why instead of hiding. */
export function TargetPicker({
  onPick,
  label = 'Add part or assembly',
}: {
  onPick: (target: RunTarget) => void;
  label?: string;
}) {
  const [open, setOpen] = useState(false);
  const catalog = useBomCatalog();

  function pick(target: RunTarget) {
    onPick(target);
    setOpen(false);
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button type="button" variant="outline" className="justify-start">
          <PlusIcon data-icon="inline-start" />
          {label}
        </Button>
      </PopoverTrigger>
      <PopoverContent
        className="w-(--radix-popover-trigger-width) min-w-80 p-0"
        align="start"
      >
        <Command>
          <CommandInput placeholder="Search parts and assemblies…" />
          <CommandList>
            <CommandEmpty>
              {catalog.isPending ? 'Loading…' : 'No matches.'}
            </CommandEmpty>
            <CommandGroup heading="Parts">
              {catalog.parts.map((part) => {
                const ready = part.printRequirementsStatus === 'Complete';
                return (
                  <CommandItem
                    key={part.id}
                    value={`part ${part.name} ${part.id}`}
                    disabled={!ready}
                    onSelect={() =>
                      pick({
                        kind: 'Part',
                        id: part.id ?? '',
                        name: part.name ?? '',
                      })
                    }
                  >
                    <TargetThumbnail
                      kind="Part"
                      thumbnailBase64={part.thumbnailBase64}
                    />
                    <div className="flex min-w-0 flex-col">
                      <span className="truncate">{part.name}</span>
                      <span className="text-xs text-muted-foreground">
                        {ready
                          ? `${part.unitsPerPrint ?? 1} per plate`
                          : 'Print requirements incomplete'}
                      </span>
                    </div>
                  </CommandItem>
                );
              })}
            </CommandGroup>
            <CommandGroup heading="Assemblies">
              {catalog.assemblies.map((assembly) => (
                <CommandItem
                  key={assembly.id}
                  value={`assembly ${assembly.name} ${assembly.id}`}
                  onSelect={() =>
                    pick({
                      kind: 'Assembly',
                      id: assembly.id ?? '',
                      name: assembly.name ?? '',
                    })
                  }
                >
                  <TargetThumbnail kind="Assembly" />
                  <span className="truncate">{assembly.name}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
