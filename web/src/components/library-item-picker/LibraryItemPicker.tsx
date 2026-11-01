import { ComponentIcon, ImageOffIcon, PlusIcon } from 'lucide-react';
import { type Ref, useEffect, useRef, useState } from 'react';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { PermissionButton as Button } from '@/components/PermissionButton';
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { Skeleton } from '@/components/ui/skeleton';
import { useAllowed } from '@/lib/auth/AccessContext';
import { cn } from '@/lib/utils';

export type LibraryTargetKind = 'Part' | 'Assembly';
export interface LibraryTarget {
  kind: LibraryTargetKind;
  id: string;
  name: string;
}
export interface LibraryPickerItem {
  id: string;
  name: string;
  thumbnailBase64?: string | null;
  description?: string;
  disabled?: boolean;
}
export interface LibraryPickerGroup {
  kind: LibraryTargetKind;
  heading: string;
  items: LibraryPickerItem[];
}

export function TargetThumbnail({
  kind,
  thumbnailBase64,
  className,
}: {
  kind: LibraryTargetKind;
  thumbnailBase64?: string | null;
  className?: string;
}) {
  return (
    <div
      className={cn(
        'flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-md bg-muted text-muted-foreground',
        className,
      )}
    >
      {kind === 'Assembly' ? (
        <ComponentIcon className="size-4" />
      ) : thumbnailBase64 ? (
        <img
          src={`data:image/png;base64,${thumbnailBase64}`}
          alt=""
          className="size-full object-contain"
        />
      ) : (
        <ImageOffIcon className="size-4" />
      )}
    </div>
  );
}

/** Shared presentation and keyboard behavior; callers own target eligibility and duplicate handling. */
export function LibraryItemPicker({
  groups,
  onPick,
  label = 'Add part or assembly',
  permission,
  disabled = false,
  shortcuts = false,
  triggerRef,
  isPending,
  isError,
  onRetry,
}: {
  groups: LibraryPickerGroup[];
  onPick: (target: LibraryTarget) => void;
  label?: string;
  permission?: string;
  disabled?: boolean;
  shortcuts?: boolean;
  triggerRef?: Ref<HTMLButtonElement>;
  isPending: boolean;
  isError: boolean;
  onRetry: () => void;
}) {
  const [open, setOpen] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const localTrigger = useRef<HTMLButtonElement>(null);
  const allowed = useAllowed(permission);
  useEffect(() => {
    if (!shortcuts || disabled || !allowed) return;
    function keyDown(event: KeyboardEvent) {
      if (event.defaultPrevented || event.altKey || open) return;
      const target = event.target instanceof HTMLElement ? event.target : null;
      if (
        target?.closest(
          '[role="dialog"], [role="alertdialog"], [role="menu"], [role="listbox"]',
        )
      )
        return;
      const typing = target?.closest(
        'input, textarea, select, [contenteditable]:not([contenteditable="false"])',
      );
      const slash =
        event.key === '/' &&
        !typing &&
        !event.metaKey &&
        !event.ctrlKey &&
        !event.shiftKey;
      const modK =
        event.key.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey);
      if (slash || modK) {
        event.preventDefault();
        setOpen(true);
      }
    }
    window.addEventListener('keydown', keyDown);
    return () => window.removeEventListener('keydown', keyDown);
  }, [shortcuts, disabled, allowed, open]);
  useEffect(() => {
    if (open && !isPending && !isError) inputRef.current?.focus();
  }, [open, isPending, isError]);
  return (
    <Popover open={open && !disabled && allowed} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          size="sm"
          permission={permission}
          disabled={disabled}
          aria-label={label}
          className="max-w-full"
          ref={(node) => {
            localTrigger.current = node;
            if (typeof triggerRef === 'function') triggerRef(node);
            else if (triggerRef) triggerRef.current = node;
          }}
        >
          <PlusIcon data-icon="inline-start" />
          {label}
          {shortcuts && (
            <kbd className="ml-1 hidden rounded bg-primary-foreground/20 px-1 text-xs sm:inline">
              /
            </kbd>
          )}
        </Button>
      </PopoverTrigger>
      <PopoverContent
        className="w-96 max-w-[calc(100vw-2rem)] p-0"
        align="end"
        aria-label="Choose a part or assembly"
        onCloseAutoFocus={(event) => {
          event.preventDefault();
          localTrigger.current?.focus();
        }}
      >
        {isPending ? (
          <div
            role="status"
            aria-label="Loading parts and assemblies"
            className="p-4"
          >
            <Skeleton className="h-24 w-full" />
          </div>
        ) : isError ? (
          <ErrorState
            size="sm"
            bordered={false}
            title="Could not load parts and assemblies."
            onRetry={onRetry}
          />
        ) : groups.every((group) => group.items.length === 0) ? (
          <EmptyState
            size="sm"
            bordered={false}
            title="No parts or assemblies"
            description="Add items to the library before choosing them here."
          />
        ) : (
          <Command label="Search parts and assemblies">
            <CommandInput
              ref={inputRef}
              aria-label="Search parts and assemblies"
              placeholder="Search parts and assemblies…"
            />
            <CommandList>
              <CommandEmpty>No matching parts or assemblies.</CommandEmpty>
              {groups
                .filter((group) => group.items.length > 0)
                .map((group) => (
                  <CommandGroup key={group.kind} heading={group.heading}>
                    {group.items.map((item) => (
                      <CommandItem
                        key={item.id}
                        value={`${group.kind}:${item.id}`}
                        keywords={[item.name]}
                        disabled={item.disabled}
                        onSelect={() => {
                          if (disabled || item.disabled) return;
                          onPick({
                            kind: group.kind,
                            id: item.id,
                            name: item.name,
                          });
                          setOpen(false);
                        }}
                      >
                        <TargetThumbnail
                          kind={group.kind}
                          thumbnailBase64={item.thumbnailBase64}
                        />
                        <div className="flex min-w-0 flex-1 flex-col">
                          <span
                            title={item.name}
                            className="line-clamp-2 break-words"
                          >
                            {item.name}
                          </span>
                          {item.description && (
                            <span className="text-xs text-muted-foreground">
                              {item.description}
                            </span>
                          )}
                        </div>
                      </CommandItem>
                    ))}
                  </CommandGroup>
                ))}
            </CommandList>
          </Command>
        )}
      </PopoverContent>
    </Popover>
  );
}
