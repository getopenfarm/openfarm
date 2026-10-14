import { MoreHorizontalIcon } from 'lucide-react';
import { type ReactNode, type Ref, useRef } from 'react';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

/** Feature callers own menu groups, eligibility, and dialogs outside the menu. */
export function DataTableRowActions({
  label,
  children,
  disabled,
  triggerRef,
  onOpen,
}: {
  label: string;
  children: ReactNode;
  disabled?: boolean;
  triggerRef?: Ref<HTMLButtonElement>;
  onOpen?: (trigger: HTMLButtonElement | null) => void;
}) {
  const localRef = useRef<HTMLButtonElement>(null);
  return (
    <div className="flex justify-end">
      <DropdownMenu
        onOpenChange={(open) => {
          if (open) onOpen?.(localRef.current);
        }}
      >
        <DropdownMenuTrigger asChild>
          <Button
            ref={(node) => {
              localRef.current = node;
              if (typeof triggerRef === 'function') triggerRef(node);
              else if (triggerRef) triggerRef.current = node;
            }}
            type="button"
            variant="ghost"
            size="icon-sm"
            aria-label={`Actions for ${label}`}
            disabled={disabled}
            onClick={(event) => event.stopPropagation()}
          >
            <MoreHorizontalIcon />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent
          align="end"
          onClick={(event) => event.stopPropagation()}
        >
          {children}
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  );
}
