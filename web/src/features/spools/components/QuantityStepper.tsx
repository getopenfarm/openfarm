import { MinusIcon, PlusIcon } from 'lucide-react';
import type { ComponentProps } from 'react';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupInput,
} from '@/components/ui/input-group';
import { cn } from '@/lib/utils';

/**
 * A whole-number field with minus and plus buttons, for a count that is usually small. Typing stays possible, so any value
 * the form's own validation should explain (0, 2.5, 51) still reaches it. It forwards the form field's id and ARIA props to
 * the input, not to the wrapper.
 */
export function QuantityStepper({
  value,
  onValueChange,
  min,
  max,
  className,
  ...inputProps
}: Omit<
  ComponentProps<typeof InputGroupInput>,
  'value' | 'onChange' | 'min' | 'max'
> & {
  value: string;
  onValueChange: (value: string) => void;
  min: number;
  max: number;
}) {
  // A blank field counts as the minimum, which is what the form sends for it.
  const current = value.trim() === '' ? min : Number(value);
  // From a typed 2.5, minus goes to 2 and plus to 3. A button at its limit only looks inactive (aria-disabled): a disabled
  // button inside the group would dim the whole group, and keeping focus on it is steadier for a keyboard.
  const step = (by: number) => {
    const base = Number.isFinite(current) ? current : min;
    const next = by < 0 ? Math.ceil(base) - 1 : Math.floor(base) + 1;
    onValueChange(String(Math.min(max, Math.max(min, next))));
  };

  return (
    <InputGroup className={cn('w-full', className)}>
      <InputGroupAddon align="inline-start">
        <InputGroupButton
          size="icon-xs"
          aria-label="Fewer"
          aria-disabled={current <= min}
          className="aria-disabled:opacity-40"
          onClick={() => step(-1)}
        >
          <MinusIcon />
        </InputGroupButton>
      </InputGroupAddon>
      <InputGroupInput
        type="number"
        inputMode="numeric"
        step="any"
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
        className="text-center tabular-nums [appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
        {...inputProps}
      />
      <InputGroupAddon align="inline-end">
        <InputGroupButton
          size="icon-xs"
          aria-label="More"
          aria-disabled={current >= max}
          className="aria-disabled:opacity-40"
          onClick={() => step(1)}
        >
          <PlusIcon />
        </InputGroupButton>
      </InputGroupAddon>
    </InputGroup>
  );
}
