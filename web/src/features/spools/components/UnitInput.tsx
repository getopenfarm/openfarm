import type { ComponentProps } from 'react';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
  InputGroupText,
} from '@/components/ui/input-group';
import { cn } from '@/lib/utils';

/**
 * A number input with its unit inside the box ("750 g"), so the field can be as narrow as the number it holds.
 * It forwards the form field's id and ARIA props to the input, not to the wrapper.
 */
export function UnitInput({
  unit,
  className,
  ...inputProps
}: ComponentProps<typeof InputGroupInput> & { unit: string }) {
  return (
    <InputGroup className={cn('w-full', className)}>
      <InputGroupInput
        type="number"
        inputMode="decimal"
        step="any"
        className="tabular-nums"
        {...inputProps}
      />
      <InputGroupAddon align="inline-end">
        <InputGroupText>{unit}</InputGroupText>
      </InputGroupAddon>
    </InputGroup>
  );
}
