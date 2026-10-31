import { MoreHorizontalIcon } from 'lucide-react';
import { type ReactNode, type Ref, useRef } from 'react';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { useAllowed } from '@/lib/auth/AccessContext';

/** Feature callers own menu groups, eligibility, and dialogs outside the menu. */
export function DataTableRowActions({
  label,
  permission,
  children,
  disabled,
  triggerRef,
  onOpen,
}: {
  label: string;
  permission?: string;
  children: ReactNode;
  disabled?: boolean;
  triggerRef?: Ref<HTMLButtonElement>;
  onOpen?: (trigger: HTMLButtonElement | null) => void;
}) {
  const allowed = useAllowed(permission);
  const localRef = useRef<HTMLButtonElement>(null);
  if (!allowed) return null;
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
