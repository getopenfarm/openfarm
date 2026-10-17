import { Command as CommandPrimitive } from 'cmdk';
import { PlusIcon, XIcon } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Badge } from '@/components/ui/badge';
import {
  CommandEmpty,
  CommandGroup,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
} from '@/components/ui/input-group';
import {
  Popover,
  PopoverAnchor,
  PopoverContent,
} from '@/components/ui/popover';
import type { ListVendorsResponse } from '@/lib/api/generated/endpoints.schemas';
import { useOpenFarmApiEndpointsVendorsListVendorsEndpoint } from '@/lib/api/generated/vendors/vendors';

const ADD = '__add';

/** An existing vendor (`vendorId`) or a typed name the parent form creates on save. */
export interface VendorValue {
  vendorId: string;
  newVendorName?: string;
}

interface VendorComboboxProps {
  id?: string;
  value: VendorValue;
  onChange: (value: VendorValue) => void;
  'aria-describedby'?: string;
  'aria-invalid'?: boolean;
}

/** Type-to-search vendor field. An unknown name offers `Add "<name>"`; nothing is created
 * until the parent form saves. */
export function VendorCombobox({
  value,
  onChange,
  ...inputProps
}: VendorComboboxProps) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  // Filter only after the user types — reopening on a picked vendor still shows the full list.
  const [typed, setTyped] = useState(false);
  const [active, setActive] = useState('');
  const [aria, setAria] = useState<{ listId?: string; activeId?: string }>({});
  const anchorRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);

  const vendorsQuery = useOpenFarmApiEndpointsVendorsListVendorsEndpoint();
  const vendors = [
    ...((vendorsQuery.data?.data as ListVendorsResponse | undefined)?.vendors ??
      []),
  ].sort((a, b) =>
    (a.name ?? '').localeCompare(b.name ?? '', undefined, {
      sensitivity: 'base',
    }),
  );

  const label =
    value.newVendorName ??
    vendors.find((vendor) => vendor.id === value.vendorId)?.name ??
    '';
  const name = query.trim();
  const needle = name.toLowerCase();
  const shown =
    typed && needle
      ? vendors.filter((vendor) => vendor.name?.toLowerCase().includes(needle))
      : vendors;
  const canAdd =
    typed &&
    needle.length > 0 &&
    !vendors.some((vendor) => vendor.name?.toLowerCase() === needle);
  // Add leads only when nothing matches, so Enter on "pru" picks Prusament, not a new "pru".
  const addFirst = canAdd && shown.length === 0;

  const order = [
    ...shown.map((vendor) => vendor.id ?? ''),
    ...(canAdd ? [ADD] : []),
  ];
  const highlighted = order.includes(active) ? active : (order[0] ?? '');

  // The input sits outside cmdk's list, so mirror its listbox id and active option for ARIA.
  // biome-ignore lint/correctness/useExhaustiveDependencies: re-read after each list render.
  useEffect(() => {
    const list = listRef.current;
    setAria({
      listId: list?.id,
      activeId: list?.querySelector('[cmdk-item][data-selected=true]')?.id,
    });
  }, [open, highlighted, query]);

  function openList() {
    if (open) return;
    setQuery(label);
    setTyped(false);
    setActive(value.vendorId);
    setOpen(true);
    // Same text before and after opening, so select now; typing replaces the picked name.
    inputRef.current?.select();
  }

  function choose(next: VendorValue) {
    onChange(next);
    setOpen(false);
  }

  const addItem = (
    <CommandGroup forceMount>
      <CommandItem
        forceMount
        value={ADD}
        className="text-primary"
        onSelect={() => choose({ vendorId: '', newVendorName: name })}
      >
        <PlusIcon className="text-primary" />
        Add "{name}"
      </CommandItem>
    </CommandGroup>
  );

  return (
    // cmdk root for arrow/Enter navigation only; filtering is ours (`shown`), and the input is a
    // plain one so the form's id/aria wiring (label, errors) survives.
    <CommandPrimitive
      shouldFilter={false}
      loop
      value={highlighted}
      onValueChange={setActive}
      className="w-full"
    >
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverAnchor asChild>
          <InputGroup ref={anchorRef}>
            <input
              ref={inputRef}
              type="text"
              role="combobox"
              aria-expanded={open}
              aria-autocomplete="list"
              aria-controls={open ? aria.listId : undefined}
              aria-activedescendant={open ? aria.activeId : undefined}
              autoComplete="off"
              data-slot="input-group-control"
              placeholder="Search or add vendor"
              className="h-full w-full min-w-0 flex-1 bg-transparent px-2.5 text-base outline-none placeholder:text-muted-foreground md:text-sm"
              value={open ? query : label}
              onChange={(event) => {
                setQuery(event.target.value);
                setTyped(true);
                setActive('');
                setOpen(true);
              }}
              onFocus={openList}
              onClick={openList}
              onKeyDown={(event) => {
                if (event.key === 'ArrowDown' && !open) openList();
              }}
              {...inputProps}
            />
            {(value.vendorId || value.newVendorName) && (
              <InputGroupAddon align="inline-end">
                {value.newVendorName && <Badge variant="secondary">New</Badge>}
                <InputGroupButton
                  size="icon-xs"
                  aria-label="Clear vendor"
                  onClick={() => {
                    onChange({ vendorId: '' });
                    setQuery('');
                    setTyped(false);
                    inputRef.current?.focus();
                  }}
                >
                  <XIcon />
                </InputGroupButton>
              </InputGroupAddon>
            )}
          </InputGroup>
        </PopoverAnchor>
        <PopoverContent
          className="w-[var(--radix-popper-anchor-width)] p-1"
          align="start"
          onOpenAutoFocus={(event) => event.preventDefault()}
          onInteractOutside={(event) => {
            // The input is the anchor, not a trigger — clicking it must not close the list.
            if (anchorRef.current?.contains(event.target as Node)) {
              event.preventDefault();
            }
          }}
        >
          <CommandList ref={listRef} aria-label="Vendors">
            {addFirst && addItem}
            {order.length === 0 && (
              <CommandEmpty>No vendors. Type a name to add one.</CommandEmpty>
            )}
            {shown.length > 0 && (
              <CommandGroup>
                {shown.map((vendor) => (
                  <CommandItem
                    key={vendor.id}
                    value={vendor.id ?? ''}
                    data-checked={vendor.id === value.vendorId}
                    onSelect={() =>
                      vendor.id && choose({ vendorId: vendor.id })
                    }
                  >
                    {vendor.name}
                  </CommandItem>
                ))}
              </CommandGroup>
            )}
            {canAdd && !addFirst && addItem}
          </CommandList>
        </PopoverContent>
      </Popover>
    </CommandPrimitive>
  );
}
