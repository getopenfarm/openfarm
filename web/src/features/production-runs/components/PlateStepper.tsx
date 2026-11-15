import { MinusIcon, PlusIcon } from 'lucide-react';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupInput,
} from '@/components/ui/input-group';

/** A whole-number field with −/+ buttons. `label` names the counted thing ("Plates"). */
export function PlateStepper({
  id,
  label,
  value,
  onChange,
  min = 1,
  disabled,
  invalid,
  describedBy,
}: {
  id?: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  min?: number;
  disabled?: boolean;
  invalid?: boolean;
  describedBy?: string;
}) {
  const current = Number.isSafeInteger(Number(value)) ? Number(value) : min;
  const step = (delta: number) =>
    onChange(String(Math.max(min, current + delta)));
  return (
    <InputGroup className="w-32 shrink-0" data-disabled={disabled || undefined}>
      <InputGroupAddon>
        <InputGroupButton
          size="icon-xs"
          aria-label={`Fewer ${label.charAt(0).toLowerCase()}${label.slice(1)}`}
          // aria-disabled, not disabled: a disabled child would fade the whole group.
          aria-disabled={current <= min}
          className="aria-disabled:opacity-50"
          disabled={disabled}
          onClick={() => current > min && step(-1)}
        >
          <MinusIcon />
        </InputGroupButton>
      </InputGroupAddon>
      <InputGroupInput
        id={id}
        aria-label={label}
        type="number"
        inputMode="numeric"
        min={min}
        step={1}
        value={value}
        disabled={disabled}
        aria-invalid={invalid}
        aria-describedby={describedBy}
        className="text-center tabular-nums [appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
        onChange={(event) => onChange(event.target.value)}
      />
      <InputGroupAddon align="inline-end">
        <InputGroupButton
          size="icon-xs"
          aria-label={`More ${label.charAt(0).toLowerCase()}${label.slice(1)}`}
          disabled={disabled}
          onClick={() => step(1)}
        >
          <PlusIcon />
        </InputGroupButton>
      </InputGroupAddon>
    </InputGroup>
  );
}
