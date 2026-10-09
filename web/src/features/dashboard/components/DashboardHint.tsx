import { type ReactNode, useEffect, useRef, useState } from 'react';
import {
  Popover,
  PopoverAnchor,
  PopoverContent,
} from '@/components/ui/popover';

/** One details surface for mouse hover, keyboard focus and touch, without duplicate tooltips. */
export function DashboardHint({
  label,
  children,
  details,
  className,
}: {
  label: string;
  children: ReactNode;
  details: ReactNode;
  className?: string;
}) {
  const [open, setOpen] = useState(false);
  const closing = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  function show() {
    clearTimeout(closing.current);
    setOpen(true);
  }
  function hide() {
    clearTimeout(closing.current);
    closing.current = setTimeout(() => setOpen(false), 150);
  }
  useEffect(() => () => clearTimeout(closing.current), []);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverAnchor asChild>
        <button
          type="button"
          className={className}
          aria-label={label}
          aria-haspopup="dialog"
          aria-expanded={open}
          onPointerEnter={(event) => {
            if (event.pointerType !== 'touch') show();
          }}
          onPointerLeave={hide}
          onFocus={show}
          onBlur={hide}
          onClick={show}
        >
          {children}
        </button>
      </PopoverAnchor>
      <PopoverContent
        aria-label={label}
        className="w-64 gap-1 text-xs"
        side="left"
        onOpenAutoFocus={(event) => event.preventDefault()}
        onCloseAutoFocus={(event) => event.preventDefault()}
        onPointerEnter={show}
        onPointerLeave={hide}
      >
        {details}
      </PopoverContent>
    </Popover>
  );
}
