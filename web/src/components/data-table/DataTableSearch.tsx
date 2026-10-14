import { SearchIcon } from 'lucide-react';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
} from '@/components/ui/input-group';

interface DataTableSearchProps {
  value: string;
  onValueChange: (value: string) => void;
  label: string;
  placeholder: string;
  disabled?: boolean;
}

export function DataTableSearch({
  value,
  onValueChange,
  label,
  placeholder,
  disabled,
}: DataTableSearchProps) {
  return (
    <InputGroup className="w-full shrink-0 sm:w-64">
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
