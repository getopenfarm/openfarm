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
import { costIconFor, costIcons } from './costIcons';

// cmdk renders a group's items in one container; lay it out as an icon grid.
const grid =
  '**:[[cmdk-group-items]]:grid **:[[cmdk-group-items]]:grid-cols-8 **:[[cmdk-group-items]]:gap-1';

export function CostIconPicker({
  icon,
  category,
  onChange,
}: {
  icon: string | null;
  category?: string | null;
  onChange: (icon: string | null) => void;
}) {
  const [open, setOpen] = useState(false);
  const Current = costIconFor(icon, category);
  const Default = costIconFor(null, category);
  const pick = (next: string | null) => {
    onChange(next);
    setOpen(false);
  };
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          size="icon"
          aria-label="Icon"
          title="Icon"
        >
          <Current />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-80 p-0" align="start">
        <Command>
          <CommandInput autoFocus placeholder="Search icons" />
          <CommandList className="max-h-96">
            <CommandEmpty>No matches.</CommandEmpty>
            <CommandGroup>
              <CommandItem
                value="default"
                keywords={['reset', 'clear']}
                data-checked={icon == null}
                onSelect={() => pick(null)}
              >
                <Default /> Default
              </CommandItem>
            </CommandGroup>
            <CommandGroup className={grid}>
              {costIcons.map((option) => {
                const Icon = option.icon;
                const selected = icon === option.name;
                return (
                  <CommandItem
                    key={option.name}
                    value={option.name}
                    keywords={[option.label, option.keywords]}
                    aria-label={
                      selected ? `${option.label}, selected` : option.label
                    }
                    title={option.label}
                    data-checked={selected}
                    onSelect={() => pick(option.name)}
                    className="size-8 justify-center p-0 data-[checked=true]:ring-2 data-[checked=true]:ring-ring [&>svg:last-child]:hidden"
                  >
                    <Icon />
                  </CommandItem>
                );
              })}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
