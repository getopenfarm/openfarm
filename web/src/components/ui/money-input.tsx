import type { ComponentProps } from 'react';
import { useCurrency } from '@/features/settings/generalSettings';
import { InputGroup, InputGroupAddon, InputGroupInput } from './input-group';

/** Numeric monetary input with the farm currency kept outside its editable value. */
export function MoneyInput({ className, ...props }: ComponentProps<'input'>) {
  const currency = useCurrency();
  return (
    <InputGroup className={className}>
      <InputGroupAddon>{currency}</InputGroupAddon>
      <InputGroupInput {...props} />
    </InputGroup>
  );
}
