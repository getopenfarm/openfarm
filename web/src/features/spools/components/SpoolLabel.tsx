import { ChevronsUpDownIcon } from 'lucide-react';
import { type ComponentProps, type ReactNode, useState } from 'react';
import { Link } from 'react-router';
import { Swatch } from '@/components/swatch/Swatch';
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
  PopoverAnchor,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import { SpoolRing } from './SpoolRing';

/** One color of one catalog filament, flat, so it can be searched by any of its names. */
export interface FilamentOption {
  id: string;
  vendor: string;
  material: string;
  colorName: string;
  colorHex: string;
}

interface SpoolLabelProps
  extends Omit<ComponentProps<'button'>, 'value' | 'onChange' | 'children'> {
  options: FilamentOption[];
  /** The chosen color's id, or '' before one is chosen. */
  value: string;
  onValueChange: (id: string) => void;
  /** What is left of the spool, 0 to 1, drawn on the ring. */
  fraction: number;
  /** What is about to be added, under the filament name. Shown once a color is chosen. */
  children?: ReactNode;
}

const hex = (value: string) => `#${value.replace(/^#/, '')}`;

/**
 * Every typed word must appear somewhere in the option, in any order ("matte bambu"). cmdk's own scoring also accepts
 * scattered letters, so "matte" would list colors that merely contain an m, an a, two t's and an e.
 */
function matchesEveryWord(value: string, search: string) {
  const haystack = value.toLowerCase();
  return search
    .toLowerCase()
    .split(/\s+/)
    .filter(Boolean)
    .every((word) => haystack.includes(word))
    ? 1
    : 0;
}

/** Catalog colors grouped under their filament, vendor then name, colors by name. */
function groupOptions(options: FilamentOption[]) {
  const collator = new Intl.Collator(undefined, { sensitivity: 'base' });
  const groups = new Map<
    string,
    { heading: string; items: FilamentOption[] }
  >();
  for (const option of [...options].sort(
    (a, b) =>
      collator.compare(a.vendor, b.vendor) ||
      collator.compare(a.material, b.material) ||
      collator.compare(a.colorName, b.colorName),
  )) {
    const heading = `${option.vendor} · ${option.material}`;
    const group = groups.get(heading) ?? { heading, items: [] };
    group.items.push(option);
    groups.set(heading, group);
  }
  return [...groups.values()];
}

/**
 * The top of the add-spool form: a spool label whose name is the filament picker. The ring shows the color and how full
 * the spool is; the lines under the name recap what is about to be added. The trigger takes the form field's id and ARIA
 * props, so the field's label and error attach to it.
 */
export function SpoolLabel({
  options,
  value,
  onValueChange,
  fraction,
  children,
  className,
  ...triggerProps
}: SpoolLabelProps) {
  const [open, setOpen] = useState(false);
  const selected = options.find((option) => option.id === value);
  const groups = groupOptions(options);

  function choose(id: string) {
    onValueChange(id);
    setOpen(false);
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverAnchor asChild>
        <div
          data-slot="spool-label"
          className={cn('flex items-center gap-4 px-5 py-4', className)}
        >
          <SpoolRing
            colorHex={selected ? hex(selected.colorHex) : undefined}
            fraction={fraction}
            className="size-16"
          />
          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <PopoverTrigger asChild>
              <button
                type="button"
                role="combobox"
                aria-expanded={open}
                aria-haspopup="listbox"
                className="-mx-2 flex min-w-0 items-center gap-2 rounded-md px-2 py-1 text-left outline-none transition-colors hover:bg-muted focus-visible:ring-3 focus-visible:ring-ring/50"
                {...triggerProps}
              >
                <span className="flex min-w-0 flex-1 flex-col">
                  <span className="truncate text-base font-medium">
                    {selected ? selected.colorName : 'Choose a filament'}
                  </span>
                  <span className="truncate text-sm text-muted-foreground">
                    {selected
                      ? `${selected.vendor} · ${selected.material}`
                      : 'Search by color, material or vendor'}
                  </span>
                </span>
                <ChevronsUpDownIcon
                  aria-hidden="true"
                  className="size-4 shrink-0 text-muted-foreground"
                />
              </button>
            </PopoverTrigger>
            {selected && children && (
              <div className="text-sm text-muted-foreground tabular-nums">
                {children}
              </div>
            )}
          </div>
        </div>
      </PopoverAnchor>
      <PopoverContent
        align="start"
        className="w-(--radix-popper-anchor-width) gap-0 p-0"
      >
        <Command filter={matchesEveryWord} label="Search filament">
          <CommandInput placeholder="Search color, material or vendor" />
          <CommandList>
            <CommandEmpty>
              {options.length === 0 ? (
                <>
                  No filament yet.{' '}
                  <Link
                    to="/filament/new"
                    className="font-medium text-primary underline-offset-4 hover:underline"
                  >
                    Add filament
                  </Link>
                </>
              ) : (
                'No filament matches.'
              )}
            </CommandEmpty>
            {groups.map((group) => (
              <CommandGroup key={group.heading} heading={group.heading}>
                {group.items.map((option) => (
                  <CommandItem
                    key={option.id}
                    value={`${option.vendor} ${option.material} ${option.colorName}`}
                    data-checked={option.id === value}
                    onSelect={() => choose(option.id)}
                  >
                    <Swatch colorHex={hex(option.colorHex)} />
                    {option.colorName}
                  </CommandItem>
                ))}
              </CommandGroup>
            ))}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
