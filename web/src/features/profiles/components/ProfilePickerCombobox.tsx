import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import type {
  FilamentProfileDetailResponse,
  FilamentProfileResponse,
  ListFilamentProfilesResponse,
  ListMachineProfilesResponse,
  ListProcessProfilesResponse,
  MachineProfileDetailResponse,
  MachineProfileResponse,
  ProcessProfileDetailResponse,
  ProcessProfileResponse,
} from '@/lib/api/generated/endpoints.schemas';
import {
  useOpenFarmApiEndpointsProfilesGetFilamentProfileEndpoint,
  useOpenFarmApiEndpointsProfilesGetMachineProfileEndpoint,
  useOpenFarmApiEndpointsProfilesGetProcessProfileEndpoint,
  useOpenFarmApiEndpointsProfilesListFilamentProfilesEndpoint,
  useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint,
  useOpenFarmApiEndpointsProfilesListProcessProfilesEndpoint,
} from '@/lib/api/generated/profiles/profiles';
import { cn } from '@/lib/utils';

export type ProfilePickerKind = 'machine' | 'process' | 'filament';

interface PickerItem {
  id: string;
  name: string;
  origin: string;
  vendorName?: string | null;
}

export interface ProfilePickerComboboxProps {
  ariaLabel?: string;
  kind: ProfilePickerKind;
  value: string | null | undefined;
  /** `name` is the picked item's display name — already known from the list render, no extra lookup. */
  onChange: (id: string | null, name?: string | null) => void;
  /** The current selection's display name — the picker has no id-to-name lookup of its own. */
  selectedLabel?: string | null;
  disabled?: boolean;
  disabledPlaceholder?: string;
  /** Trigger button text when nothing is selected. */
  placeholder?: string;
  /** Search input placeholder — distinct from the trigger's empty-state text above. */
  searchPlaceholder?: string;
  /** process/filament only (contracts/profiles-api.md). */
  compatibleWithMachineName?: string | null;
  /** process only — sugar over compatibleWithMachineName. */
  printerId?: string | null;
  /** filament only. */
  materialType?: string | null;
}

function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const handle = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(handle);
  }, [value, delayMs]);
  return debounced;
}

function toPickerItem(
  item:
    | MachineProfileResponse
    | ProcessProfileResponse
    | FilamentProfileResponse,
): PickerItem | null {
  if (!item.id || !item.name) {
    return null;
  }
  return {
    id: item.id,
    name: item.name,
    origin: item.origin ?? 'User',
    vendorName: item.vendorName,
  };
}

/**
 * Shared searchable, paginated, origin-labeled picker for Machine/Process/Filament profiles
 * (SC-008/SC-010) — one component backing the printer edit dialog's machine/process fields and the
 * part detail page's filament narrowing control, rather than bespoke per-page rendering. Calls the
 * extended `GET /api/v1/profiles/{machine,process,filament}` routes with `origin=All` so both System
 * and User rows are selectable, labeled by origin.
 */
export function ProfilePickerCombobox({
  ariaLabel,
  kind,
  value,
  onChange,
  selectedLabel,
  disabled,
  disabledPlaceholder = 'Select a machine profile first',
  placeholder = 'Select a profile…',
  searchPlaceholder = 'Search profiles…',
  compatibleWithMachineName,
  printerId,
  materialType,
}: ProfilePickerComboboxProps) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  const debouncedSearch = useDebouncedValue(search, 250);
  const q = debouncedSearch.trim() || undefined;

  const machineQuery =
    useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint(
      { origin: 'All', q, page: 1, pageSize: 50 },
      { query: { enabled: open && kind === 'machine' } },
    );
  const processQuery =
    useOpenFarmApiEndpointsProfilesListProcessProfilesEndpoint(
      {
        origin: 'All',
        q,
        compatibleWithMachineName: compatibleWithMachineName ?? undefined,
        printerId: printerId ?? undefined,
        page: 1,
        pageSize: 50,
      },
      { query: { enabled: open && kind === 'process' } },
    );
  const filamentQuery =
    useOpenFarmApiEndpointsProfilesListFilamentProfilesEndpoint(
      {
        origin: 'All',
        q,
        compatibleWithMachineName: compatibleWithMachineName ?? undefined,
        materialType: materialType ?? undefined,
        page: 1,
        pageSize: 50,
      },
      { query: { enabled: open && kind === 'filament' } },
    );

  // Resolves the current selection's display name when the caller doesn't already know it
  // (`selectedLabel` omitted) — a single GET-by-id lookup, gated to only run when actually needed.
  const needsLabelLookup = Boolean(value) && selectedLabel === undefined;
  const machineDetailQuery =
    useOpenFarmApiEndpointsProfilesGetMachineProfileEndpoint(value ?? '', {
      query: { enabled: needsLabelLookup && kind === 'machine' },
    });
  const processDetailQuery =
    useOpenFarmApiEndpointsProfilesGetProcessProfileEndpoint(value ?? '', {
      query: { enabled: needsLabelLookup && kind === 'process' },
    });
  const filamentDetailQuery =
    useOpenFarmApiEndpointsProfilesGetFilamentProfileEndpoint(value ?? '', {
      query: { enabled: needsLabelLookup && kind === 'filament' },
    });
  const resolvedLabel = needsLabelLookup
    ? ((kind === 'machine'
        ? (machineDetailQuery.data?.data as
            | MachineProfileDetailResponse
            | undefined)
        : kind === 'process'
          ? (processDetailQuery.data?.data as
              | ProcessProfileDetailResponse
              | undefined)
          : (filamentDetailQuery.data?.data as
              | FilamentProfileDetailResponse
              | undefined)
      )?.name ?? null)
    : (selectedLabel ?? null);

  const isLoading =
    kind === 'machine'
      ? machineQuery.isLoading
      : kind === 'process'
        ? processQuery.isLoading
        : filamentQuery.isLoading;

  const items: PickerItem[] =
    (kind === 'machine'
      ? (machineQuery.data?.data as ListMachineProfilesResponse | undefined)
          ?.items
      : kind === 'process'
        ? (processQuery.data?.data as ListProcessProfilesResponse | undefined)
            ?.items
        : (filamentQuery.data?.data as ListFilamentProfilesResponse | undefined)
            ?.items
    )
      ?.map(toPickerItem)
      .filter((item): item is PickerItem => item !== null) ?? [];

  function handleSelect(id: string | null, name: string | null) {
    onChange(id, name);
    setOpen(false);
    setSearch('');
  }

  return (
    <Popover
      open={open}
      onOpenChange={(nextOpen) => {
        if (disabled) {
          return;
        }
        setOpen(nextOpen);
        if (!nextOpen) {
          setSearch('');
        }
      }}
    >
      <PopoverTrigger asChild>
        <Button
          type="button"
          aria-label={ariaLabel}
          variant="outline"
          disabled={disabled}
          className="w-full justify-start font-normal"
        >
          {disabled ? disabledPlaceholder : (resolvedLabel ?? placeholder)}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-80 p-0" align="start">
        <div className="flex flex-col gap-2 p-2">
          <Input
            autoFocus
            placeholder={searchPlaceholder}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
          <div className="flex max-h-64 flex-col gap-0.5 overflow-y-auto">
            <button
              type="button"
              onClick={() => handleSelect(null, null)}
              className={cn(
                'flex items-center rounded-md px-2 py-1.5 text-left text-sm hover:bg-muted',
                value === null || value === undefined
                  ? 'bg-muted font-medium'
                  : '',
              )}
            >
              None
            </button>
            {isLoading && (
              <p className="px-2 py-1.5 text-xs text-muted-foreground">
                Loading…
              </p>
            )}
            {!isLoading && items.length === 0 && (
              <p className="px-2 py-1.5 text-xs text-muted-foreground">
                No profiles found.
              </p>
            )}
            {items.map((item) => (
              <button
                key={item.id}
                type="button"
                onClick={() => handleSelect(item.id, item.name)}
                className={cn(
                  'flex items-center justify-between gap-2 rounded-md px-2 py-1.5 text-left text-sm hover:bg-muted',
                  value === item.id ? 'bg-muted font-medium' : '',
                )}
              >
                <span className="truncate">{item.name}</span>
                <span className="shrink-0 text-xs text-muted-foreground">
                  {item.origin === 'System'
                    ? `System${item.vendorName ? `: ${item.vendorName}` : ''}`
                    : 'User'}
                </span>
              </button>
            ))}
          </div>
        </div>
      </PopoverContent>
    </Popover>
  );
}
