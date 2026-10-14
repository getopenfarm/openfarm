import type { ComponentProps, ReactNode } from 'react';
import { TabsList } from '@/components/ui/tabs';

interface DataTableToolbarProps {
  children?: ReactNode;
  search?: ReactNode;
  actions?: ReactNode;
}

export function DataTableToolbar({
  children,
  search,
  actions,
}: DataTableToolbarProps) {
  return (
    <div
      data-slot="table-toolbar"
      className="flex w-full min-w-0 shrink-0 flex-wrap items-center gap-3"
    >
      {children}
      {(search || actions) && (
        <div className="ml-auto flex w-full min-w-0 flex-wrap items-center justify-end gap-3 sm:w-auto">
          {search}
          {actions}
        </div>
      )}
    </div>
  );
}

export function DataTableTabsList(
  props: Omit<ComponentProps<typeof TabsList>, 'className' | 'variant'>,
) {
  return (
    <TabsList
      className="max-w-full flex-wrap justify-start group-data-horizontal/tabs:h-auto [&>[data-slot=tabs-trigger]]:h-7 [&>[data-slot=tabs-trigger]]:flex-none"
      {...props}
    />
  );
}
