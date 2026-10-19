import type { ReactNode, Ref } from 'react';
import { useId } from 'react';
import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { cn } from '@/lib/utils';
import {
  isService,
  physicalUnits,
  serviceUnits,
  unitLabels,
  unitNames,
} from './costing';

export function Choice({
  label,
  value,
  options,
  onChange,
  disabled,
  triggerRef,
  hideLabel = false,
}: {
  label: string;
  value: string;
  options: readonly (string | { value: string; label: string })[];
  onChange: (value: string) => void;
  disabled?: boolean;
  triggerRef?: Ref<HTMLButtonElement>;
  hideLabel?: boolean;
}) {
  const controlId = useId();
  return (
    <Field data-disabled={disabled}>
      <FieldLabel
        htmlFor={controlId}
        className={hideLabel ? 'sr-only' : undefined}
      >
        {label}
      </FieldLabel>
      <Select value={value} onValueChange={onChange} disabled={disabled}>
        <SelectTrigger id={controlId} ref={triggerRef}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            {options.map((option) => {
              const item =
                typeof option === 'string'
                  ? { value: option, label: option }
                  : option;
              return (
                <SelectItem key={item.value} value={item.value}>
                  {item.label}
                </SelectItem>
              );
            })}
          </SelectGroup>
        </SelectContent>
      </Select>
    </Field>
  );
}

// Consumption unit for a cost item. The trigger shows the short label (m²),
// the menu shows the full names grouped by item type.
export function UnitSelect({
  id,
  value,
  onChange,
  disabled,
}: {
  id?: string;
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
}) {
  return (
    <Select value={value} onValueChange={onChange} disabled={disabled}>
      <SelectTrigger id={id} aria-label="Unit" className="w-full">
        <SelectValue>
          {isService(value) ? unitNames[value] : unitLabels[value]}
        </SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectGroup>
          <SelectLabel>Physical</SelectLabel>
          {physicalUnits.map((unit) => (
            <SelectItem key={unit} value={unit}>
              {unitNames[unit]}
            </SelectItem>
          ))}
        </SelectGroup>
        <SelectGroup>
          <SelectLabel>Service</SelectLabel>
          {serviceUnits.map((unit) => (
            <SelectItem key={unit} value={unit}>
              {unitNames[unit]}
            </SelectItem>
          ))}
        </SelectGroup>
      </SelectContent>
    </Select>
  );
}

export function NumberField({
  label,
  value,
  onChange,
  whole = false,
  required = false,
  disabled = false,
  error,
  description,
  placeholder,
  adornment,
  className,
}: {
  label: string;
  value: number | null | undefined;
  onChange: (value: number | null) => void;
  whole?: boolean;
  required?: boolean;
  disabled?: boolean;
  error?: string;
  description?: string;
  placeholder?: string;
  /** Control rendered beside the input (a unit select); the error stays below both. */
  adornment?: ReactNode;
  className?: string;
}) {
  const controlId = useId();
  return (
    <Field
      data-disabled={disabled}
      data-invalid={!!error || undefined}
      className={className}
    >
      <FieldLabel htmlFor={controlId}>{label}</FieldLabel>
      <div className="flex items-start gap-2">
        <Input
          id={controlId}
          aria-invalid={!!error || undefined}
          aria-describedby={
            [
              description ? `${controlId}-description` : '',
              error ? `${controlId}-error` : '',
            ]
              .filter(Boolean)
              .join(' ') || undefined
          }
          type="number"
          min={required ? (whole ? 1 : '0.000000001') : 0}
          step={whole ? 1 : 'any'}
          required={required}
          disabled={disabled}
          placeholder={placeholder}
          value={value ?? ''}
          onChange={(event) =>
            onChange(
              event.target.value === '' ? null : Number(event.target.value),
            )
          }
        />
        {adornment && (
          <div className="w-28 shrink-0 text-foreground">{adornment}</div>
        )}
      </div>
      {description && (
        <FieldDescription id={`${controlId}-description`}>
          {description}
        </FieldDescription>
      )}
      {error && <FieldError id={`${controlId}-error`}>{error}</FieldError>}
    </Field>
  );
}
export function DimensionFields({
  length,
  lengthUnit,
  width,
  widthUnit,
  onChange,
  children,
  errors,
  className,
}: {
  length?: number | null;
  lengthUnit?: string | null;
  width?: number | null;
  widthUnit?: string | null;
  onChange: (patch: {
    length?: number | null;
    lengthUnit?: string;
    width?: number | null;
    widthUnit?: string;
  }) => void;
  children?: ReactNode;
  className?: string;
  errors?: { length?: string; width?: string };
}) {
  return (
    <Field className={cn('grid grid-cols-1 gap-4 sm:grid-cols-2', className)}>
      <NumberField
        label="Length"
        error={errors?.length}
        value={length}
        required
        onChange={(length) => onChange({ length })}
        adornment={
          <Choice
            hideLabel
            label="Length unit"
            value={lengthUnit ?? 'cm'}
            options={['mm', 'cm', 'm']}
            onChange={(lengthUnit) => onChange({ lengthUnit })}
          />
        }
      />
      <NumberField
        label="Width"
        error={errors?.width}
        value={width}
        required
        onChange={(width) => onChange({ width })}
        adornment={
          <Choice
            hideLabel
            label="Width unit"
            value={widthUnit ?? 'cm'}
            options={['mm', 'cm', 'm']}
            onChange={(widthUnit) => onChange({ widthUnit })}
          />
        }
      />
      {children}
    </Field>
  );
}
