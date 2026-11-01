import type { ReactNode } from 'react';
import { PageHeader } from '@/components/page-header/PageHeader';
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
} from '@/components/ui/field';
import { cn } from '@/lib/utils';

interface SettingsSectionProps {
  title: ReactNode;
  description?: ReactNode;
  /** Trailing action in the head, e.g. an Add button. */
  action?: ReactNode;
  children: ReactNode;
  className?: string;
}

/** A settings pane: 640px column with a 16px title and a muted description. */
export function SettingsSection({
  title,
  description,
  action,
  children,
  className,
}: SettingsSectionProps) {
  return (
    <section className={cn('flex max-w-[640px] flex-col', className)}>
      <PageHeader
        size="section"
        title={title}
        description={description}
        action={action}
        className="mb-4"
      />
      {children}
    </section>
  );
}

/** Bordered card holding a stack of SettingsRow. */
export function SettingsRows({
  className,
  ...props
}: React.ComponentProps<'div'>) {
  return (
    <FieldGroup
      className={cn(
        'gap-0 divide-y rounded-lg border bg-card px-3.5 md:px-[18px]',
        className,
      )}
      {...props}
    />
  );
}

interface SettingsRowProps {
  label: ReactNode;
  help?: ReactNode;
  htmlFor?: string;
  /** 260px control instead of 220px. */
  wide?: boolean;
  children: ReactNode;
  invalid?: boolean;
  disabled?: boolean;
}

/** Label and help on the left, control on the right. */
export function SettingsRow({
  label,
  help,
  htmlFor,
  wide,
  children,
  invalid,
  disabled,
}: SettingsRowProps) {
  return (
    <Field
      orientation="horizontal"
      data-invalid={invalid || undefined}
      data-disabled={disabled || undefined}
      className="flex-col items-stretch gap-2.5 py-4 md:flex-row md:items-center md:justify-between md:gap-6"
    >
      <div className="min-w-0">
        <FieldLabel htmlFor={htmlFor}>{label}</FieldLabel>
        {help ? (
          <FieldDescription className="mt-0.5">{help}</FieldDescription>
        ) : null}
      </div>
      <div
        className={cn(
          'w-full shrink-0',
          wide ? 'md:w-[260px]' : 'md:w-[220px]',
        )}
      >
        {children}
      </div>
    </Field>
  );
}

/** Small muted heading that groups rows. */
export function SettingsSubhead({ children }: { children: ReactNode }) {
  return (
    <p className="mt-[18px] mb-2 text-[11px] font-semibold tracking-[0.06em] text-muted-foreground uppercase">
      {children}
    </p>
  );
}

/** Muted hint under a block of rows. */
export function SettingsHint({ children }: { children: ReactNode }) {
  return (
    <p className="mt-3 max-w-[62ch] text-xs leading-normal text-muted-foreground">
      {children}
    </p>
  );
}
