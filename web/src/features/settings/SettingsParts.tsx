import type { ReactNode } from 'react';
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
      <div className="mb-[18px] flex items-center justify-between gap-4">
        <div>
          <h2 className="font-heading text-base font-semibold">{title}</h2>
          {description ? (
            <p className="text-[13px] text-muted-foreground">{description}</p>
          ) : null}
        </div>
        {action}
      </div>
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
    <div
      className={cn(
        'divide-y rounded-lg border bg-card px-3.5 md:px-[18px]',
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
}

/** Label and help on the left, control on the right. */
export function SettingsRow({
  label,
  help,
  htmlFor,
  wide,
  children,
}: SettingsRowProps) {
  return (
    <div className="flex flex-col gap-2.5 py-4 md:flex-row md:items-center md:justify-between md:gap-6">
      <div className="min-w-0">
        <label htmlFor={htmlFor} className="text-[13.5px] font-medium">
          {label}
        </label>
        {help ? (
          <p className="mt-0.5 text-xs text-muted-foreground">{help}</p>
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
    </div>
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
