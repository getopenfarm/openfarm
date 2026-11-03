import {
  CalendarIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  XIcon,
} from 'lucide-react';
import { type Ref, useRef, useState } from 'react';
import { DayPicker } from 'react-day-picker';
import { Button, buttonVariants } from '@/components/ui/button';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { cn } from '@/lib/utils';

function dateValue(value?: string) {
  if (!value) return undefined;
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day);
}
function isoDay(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

/** Calendar popover with the same compact trigger and separate clear button as Akaun. */
export function DatePicker({
  id,
  value,
  onChange,
  onBlur,
  min,
  disabled = false,
  placeholder = 'Pick a date',
  clearLabel = 'Clear date',
  describedBy,
  ref,
}: {
  id: string;
  value: string;
  onChange: (value: string) => void;
  onBlur?: () => void;
  min?: string;
  disabled?: boolean;
  placeholder?: string;
  clearLabel?: string;
  describedBy?: string;
  ref?: Ref<HTMLButtonElement>;
}) {
  const [open, setOpen] = useState(false);
  const selected = dateValue(value);
  const minimum = dateValue(min);
  const [month, setMonth] = useState(selected ?? minimum ?? new Date());
  const trigger = useRef<HTMLButtonElement | null>(null);
  const display = selected
    ? new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium' }).format(selected)
    : placeholder;
  return (
    <div className="relative w-[260px] max-w-full">
      <Popover
        open={open}
        onOpenChange={(next) => {
          if (next) setMonth(dateValue(value) ?? minimum ?? new Date());
          setOpen(next);
        }}
      >
        <PopoverTrigger asChild>
          <Button
            id={id}
            ref={(node) => {
              trigger.current = node;
              if (typeof ref === 'function') ref(node);
              else if (ref) ref.current = node;
            }}
            type="button"
            variant="outline"
            disabled={disabled}
            onBlur={onBlur}
            aria-describedby={describedBy}
            className={cn(
              'h-9 w-full justify-start gap-2 rounded-md bg-card px-3 text-left text-[13.5px] font-normal',
              selected && 'pr-[34px]',
              !selected && 'text-muted-foreground',
            )}
          >
            <CalendarIcon data-icon="inline-start" className="opacity-50" />
            {display}
          </Button>
        </PopoverTrigger>
        <PopoverContent
          align="start"
          className="w-auto p-0"
          role="dialog"
          aria-label="Choose expiry date"
        >
          <DayPicker
            mode="single"
            required
            selected={selected}
            month={month}
            onMonthChange={setMonth}
            autoFocus
            showOutsideDays
            formatters={{
              formatWeekdayName: (date) =>
                new Intl.DateTimeFormat('en-US', { weekday: 'short' }).format(
                  date,
                ),
            }}
            disabled={minimum ? { before: minimum } : undefined}
            startMonth={minimum}
            onSelect={(date) => {
              if (date) {
                onChange(isoDay(date));
                setOpen(false);
              }
            }}
            components={{
              Chevron: ({ orientation }) =>
                orientation === 'left' ? (
                  <ChevronLeftIcon className="size-4" />
                ) : (
                  <ChevronRightIcon className="size-4" />
                ),
            }}
            classNames={{
              root: 'p-3',
              months: 'relative flex flex-col',
              month: 'flex flex-col gap-3',
              month_caption: 'flex h-8 items-center justify-center px-9',
              caption_label: 'text-sm font-medium',
              nav: 'absolute inset-x-0 top-0 flex justify-between',
              button_previous: cn(
                buttonVariants({ variant: 'ghost', size: 'icon' }),
                'size-8',
              ),
              button_next: cn(
                buttonVariants({ variant: 'ghost', size: 'icon' }),
                'size-8',
              ),
              month_grid: 'border-collapse',
              weekdays: 'flex',
              weekday:
                'w-9 text-center text-xs font-normal text-muted-foreground',
              week: 'mt-1 flex',
              day: 'relative size-9 p-0 text-center text-sm',
              day_button: cn(
                buttonVariants({ variant: 'ghost', size: 'icon-lg' }),
                'rounded-md font-normal',
              ),
              selected:
                '[&>button]:bg-primary [&>button]:text-primary-foreground [&>button]:hover:bg-primary [&>button]:hover:text-primary-foreground',
              today:
                '[&:not([data-selected=true])>button]:bg-accent [&:not([data-selected=true])>button]:text-accent-foreground',
              outside: 'text-muted-foreground opacity-50',
              disabled: 'opacity-40',
              hidden: 'invisible',
            }}
          />
        </PopoverContent>
      </Popover>
      {value && !disabled && (
        <Button
          type="button"
          variant="ghost"
          size="icon-xs"
          className="absolute top-1/2 right-1.5 -translate-y-1/2 text-muted-foreground"
          aria-label={clearLabel}
          title={clearLabel}
          onClick={() => {
            onChange('');
            trigger.current?.focus();
          }}
        >
          <XIcon />
        </Button>
      )}
    </div>
  );
}
