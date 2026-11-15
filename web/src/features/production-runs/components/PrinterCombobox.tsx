import { ChevronsUpDownIcon } from 'lucide-react';
import { useState } from 'react';
import {
  type PrinterStatus,
  STATUS_META,
} from '@/components/printer-tile/status';
import {
  BADGE_KIND_META,
  type BadgeKind,
} from '@/components/status-badge/kinds';
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
import { printerStatusToBadge } from '@/features/printers/lib/printerStatusBadge';
import type { FleetPrinterResponse } from '@/lib/api/generated/endpoints.schemas';
import { formatMinutes } from '@/lib/formatMinutes';
import { cn } from '@/lib/utils';

export const AUTOMATIC = 'automatic';

/** "Ready", "Printing · 42m left", "Offline": what the printer is doing now. */
export function printerState(printer: FleetPrinterResponse) {
  const badge = printerStatusToBadge(
    printer.status as PrinterStatus,
    printer.isReachable ?? false,
    undefined,
    printer.setupIssues,
  );
  const left =
    printer.status === 'Printing' && printer.isReachable
      ? printer.remainingTimeMinutes
      : null;
  return {
    kind: badge.kind,
    idle: badge.label === STATUS_META.Idle.label,
    label:
      left != null
        ? `${badge.label} · ${formatMinutes(left)} left`
        : badge.label,
  };
}

function Dot({ kind }: { kind?: BadgeKind }) {
  return (
    <span
      aria-hidden
      className={cn(
        'size-2 shrink-0 rounded-full',
        kind ? BADGE_KIND_META[kind].dotClass : 'bg-primary',
      )}
    />
  );
}

/** Automatic, or one printer from the fleet, searchable, with each printer's live state. */
export function PrinterCombobox({
  id,
  printers,
  value,
  onChange,
  disabled,
}: {
  id?: string;
  printers: FleetPrinterResponse[];
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const selected = printers.find((printer) => printer.id === value);
  const state = selected && printerState(selected);
  function choose(next: string) {
    onChange(next);
    setOpen(false);
  }
  return (
    <Popover open={open} onOpenChange={(next) => !disabled && setOpen(next)}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          id={id}
          variant="ghost"
          role="combobox"
          aria-expanded={open}
          disabled={disabled}
          className="-mx-2.5 w-[calc(100%+1.25rem)] min-w-0 justify-start gap-2 font-normal"
        >
          <Dot kind={state?.kind} />
          <span className="truncate">{selected?.name ?? 'Automatic'}</span>
          {state && (
            <span className="shrink-0 text-muted-foreground">
              {state.label}
            </span>
          )}
          <ChevronsUpDownIcon className="ml-auto text-muted-foreground" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        className="w-[var(--radix-popover-trigger-width)] min-w-72 p-0"
        align="start"
      >
        <Command>
          <CommandInput placeholder="Search printers…" />
          <CommandList aria-label="Printers">
            <CommandEmpty>No printers found.</CommandEmpty>
            <CommandGroup>
              <CommandItem
                value="Automatic any matching printer"
                data-checked={value === AUTOMATIC}
                onSelect={() => choose(AUTOMATIC)}
              >
                <Dot />
                <span className="flex-1">Automatic</span>
                <span className="text-muted-foreground">
                  Any matching printer
                </span>
              </CommandItem>
            </CommandGroup>
            {printers.length > 0 && (
              <CommandGroup heading="Printers">
                {printers.map((printer) => {
                  const row = printerState(printer);
                  return (
                    <CommandItem
                      key={printer.id}
                      value={`${printer.name} ${printer.model?.name ?? ''} ${printer.id}`}
                      data-checked={value === printer.id}
                      onSelect={() => choose(printer.id ?? AUTOMATIC)}
                    >
                      <Dot kind={row.kind} />
                      <span className="flex min-w-0 flex-1 items-baseline gap-2">
                        <span className="truncate">{printer.name}</span>
                        {printer.model?.name && (
                          <span className="truncate text-xs text-muted-foreground">
                            {printer.model.name}
                          </span>
                        )}
                      </span>
                      <span className="shrink-0 text-muted-foreground tabular-nums">
                        {row.label}
                      </span>
                    </CommandItem>
                  );
                })}
              </CommandGroup>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
