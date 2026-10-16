import { type ReactNode, useId } from 'react';
import { Button } from '@/components/ui/button';
import { Separator } from '@/components/ui/separator';

interface DataTableSelectionBarProps {
  selectedCount: number;
  pending?: boolean;
  onClear: () => void;
  label: string;
  children: ReactNode;
}

/** Bulk actions for DataTable's `toolbar` slot; renders nothing visible until rows are selected. */
export function DataTableSelectionBar({
  selectedCount,
  pending = false,
  onClear,
  label,
  children,
}: DataTableSelectionBarProps) {
  const countId = useId();
  return (
    <>
      <span id={countId} role="status" className="sr-only">
        {selectedCount} selected
      </span>
      {selectedCount > 0 && (
        <section
          data-slot="table-selection-bar"
          aria-label={label}
          aria-describedby={countId}
          className="flex min-w-0 flex-1"
        >
          <fieldset
            disabled={pending}
            className="flex min-w-0 flex-1 flex-wrap items-center gap-2"
          >
            <span
              aria-hidden="true"
              className="text-sm font-medium whitespace-nowrap"
            >
              {selectedCount} selected
            </span>
            <Separator orientation="vertical" className="h-4" />
            {children}
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="ml-auto"
              disabled={pending}
              onClick={onClear}
            >
              Clear selection
            </Button>
          </fieldset>
        </section>
      )}
    </>
  );
}
