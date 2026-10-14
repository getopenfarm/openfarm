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

/** Keep bulk controls visible while scrolling. The page reserves space below its pagination. */
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
          className="fixed bottom-[calc(1rem+env(safe-area-inset-bottom))] left-1/2 z-30 flex w-max max-w-[calc(100%-2rem)] -translate-x-1/2 flex-wrap items-center justify-center gap-2 rounded-lg border bg-popover p-2 text-popover-foreground shadow-lg"
        >
          <span aria-hidden="true" className="px-2 text-sm whitespace-nowrap">
            {selectedCount} selected
          </span>
          <Separator orientation="vertical" />
          <fieldset
            disabled={pending}
            aria-label="Bulk actions"
            className="flex flex-wrap items-center justify-center gap-2"
          >
            {children}
            <Button
              type="button"
              variant="ghost"
              size="sm"
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
