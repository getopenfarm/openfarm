import { SearchIcon } from 'lucide-react';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
} from '@/components/ui/input-group';
import { cn } from '@/lib/utils';

interface DataTableSearchProps {
  value: string;
  onValueChange: (value: string) => void;
  label: string;
  placeholder: string;
  disabled?: boolean;
  className?: string;
}

export function DataTableSearch({
  value,
  onValueChange,
  label,
  placeholder,
  disabled,
  className,
}: DataTableSearchProps) {
  return (
    <InputGroup className={cn('w-full shrink-0 sm:w-64', className)}>
      <InputGroupInput
        aria-label={label}
        placeholder={placeholder}
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
        disabled={disabled}
      />
      <InputGroupAddon>
        <SearchIcon aria-hidden="true" />
      </InputGroupAddon>
    </InputGroup>
  );
}
