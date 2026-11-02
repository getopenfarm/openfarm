import { MoreHorizontalIcon } from 'lucide-react';
import { type ReactNode, type Ref, useEffect, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { useAllowed } from '@/lib/auth/AccessContext';

/** Right-click target: a table row, or any element that opts in (e.g. a library tile). */
const SCOPE = 'tr, [data-row-actions-scope]';
/** Keep the browser menu where it is useful (open link, copy/paste). */
const NATIVE = 'a[href], input, textarea, [contenteditable]';

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
  const [point, setPoint] = useState<{ x: number; y: number } | null>(null);

  useEffect(() => {
    if (!allowed || disabled) return;
    const scope = localRef.current?.closest<HTMLElement>(SCOPE);
    if (!scope) return;
    const handler = (event: MouseEvent) => {
      if ((event.target as Element | null)?.closest(NATIVE)) return;
      event.preventDefault();
      onOpen?.(localRef.current);
      setPoint({ x: event.clientX, y: event.clientY });
    };
    scope.addEventListener('contextmenu', handler);
    return () => scope.removeEventListener('contextmenu', handler);
  }, [allowed, disabled, onOpen]);

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
      {point && (
        <DropdownMenu
          open
          onOpenChange={(open) => {
            if (!open) setPoint(null);
          }}
        >
          <DropdownMenuTrigger asChild>
            <span
              aria-hidden
              tabIndex={-1}
              className="pointer-events-none fixed size-0"
              style={{ left: point.x, top: point.y }}
            />
          </DropdownMenuTrigger>
          <DropdownMenuContent
            align="start"
            sideOffset={0}
            onClick={(event) => event.stopPropagation()}
            onCloseAutoFocus={(event) => {
              // Return focus to the ⋯ button, not the invisible anchor.
              event.preventDefault();
              localRef.current?.focus();
            }}
          >
            {children}
          </DropdownMenuContent>
        </DropdownMenu>
      )}
    </div>
  );
}
