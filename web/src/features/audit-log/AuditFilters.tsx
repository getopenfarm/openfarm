import { CalendarIcon, ChevronDownIcon, type LucideIcon } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Field, FieldGroup, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';

export interface AuditFilterSection {
  label: string;
  value: string;
  options: [string, string][];
  onChange: (value: string) => void;
}
interface FilterProps {
  label: string;
  icon: LucideIcon;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  sections: AuditFilterSection[];
}

/** Groups remain open after a choice, matching Akaun's filter workflow. */
export function AuditFilterMenu({
  label,
  icon: Icon,
  open,
  onOpenChange,
  sections,
}: FilterProps) {
  const active = sections.filter((s) => s.value).length;
  return (
    <DropdownMenu open={open} onOpenChange={onOpenChange} modal={false}>
      <DropdownMenuTrigger asChild>
        <Button
          variant="outline"
          size="sm"
          aria-label={label}
          className="max-w-full"
        >
          <Icon data-icon="inline-start" />
          <span className="max-w-44 truncate">{label}</span>
          {active > 0 && <Badge kind="blue">{active}</Badge>}
          <ChevronDownIcon data-icon="inline-end" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="end"
        className="w-64 min-w-0 max-w-[calc(100vw-2rem)]"
      >
        {sections.map((section, index) => (
          <DropdownMenuGroup key={section.label}>
            {index > 0 && <DropdownMenuSeparator />}
            <div className="flex items-center justify-between gap-3">
              <DropdownMenuLabel>{section.label}</DropdownMenuLabel>
              {section.value && (
                <DropdownMenuItem
                  aria-label={`Clear ${section.label.toLowerCase()} filter`}
                  onSelect={(event) => {
                    event.preventDefault();
                    section.onChange('');
                    onOpenChange(false);
                  }}
                >
                  Clear
                </DropdownMenuItem>
              )}
            </div>
            <DropdownMenuRadioGroup
              value={section.value}
              onValueChange={section.onChange}
              aria-label={section.label}
            >
              {section.options.map(([value, text]) => (
                <DropdownMenuRadioItem
                  key={value}
                  value={value}
                  onSelect={(event) => event.preventDefault()}
                >
                  {text}
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
            {section.options.length === 0 && (
              <DropdownMenuItem disabled>Nothing yet</DropdownMenuItem>
            )}
          </DropdownMenuGroup>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

export function AuditDateFilter({
  from,
  to,
  open,
  onOpenChange,
  onChange,
}: {
  from: string;
  to: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onChange: (from: string, to: string) => void;
}) {
  const active = !!(from || to);
  return (
    <Popover open={open} onOpenChange={onOpenChange}>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" aria-label="Date">
          <CalendarIcon data-icon="inline-start" />
          Date{active && <Badge kind="blue">1</Badge>}
          <ChevronDownIcon data-icon="inline-end" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        align="end"
        className="max-w-[calc(100vw-2rem)]"
        aria-label="Date range"
      >
        <div className="flex items-center justify-between gap-3">
          <span className="font-medium">Date range</span>
          {active && (
            <Button
              size="sm"
              variant="ghost"
              onClick={() => {
                onChange('', '');
                onOpenChange(false);
              }}
            >
              Clear dates
            </Button>
          )}
        </div>
        <FieldGroup>
          <Field>
            <FieldLabel htmlFor="audit-from">From</FieldLabel>
            <Input
              id="audit-from"
              type="date"
              value={from}
              max={to || undefined}
              onChange={(event) => onChange(event.target.value, to)}
            />
          </Field>
          <Field>
            <FieldLabel htmlFor="audit-to">To</FieldLabel>
            <Input
              id="audit-to"
              type="date"
              value={to}
              min={from || undefined}
              onChange={(event) => onChange(from, event.target.value)}
            />
          </Field>
        </FieldGroup>
        <p className="text-xs text-muted-foreground">
          {Intl.DateTimeFormat().resolvedOptions().timeZone}
        </p>
      </PopoverContent>
    </Popover>
  );
}
