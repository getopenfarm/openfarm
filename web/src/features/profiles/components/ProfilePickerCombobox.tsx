import { ChevronsUpDownIcon } from 'lucide-react';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import {
  Command,
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
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
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

export type ProfilePickerKind = 'machine' | 'process' | 'filament';

interface PickerItem {
  id: string;
  name: string;
  origin: string;
  vendorName?: string | null;
  sourceFormat?: string | null;
  sourceVersion?: string | null;
}

export interface ProfilePickerComboboxProps {
  ariaLabel?: string;
  id?: string;
  ref?: React.Ref<HTMLButtonElement>;
  'aria-describedby'?: string;
  'aria-invalid'?: boolean;
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
  brand?: string;
  modelCode?: string;
  nozzleDiameterMm?: number;
  /** filament only. */
  materialType?: string | null;
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
    sourceFormat: item.sourceFormat,
    sourceVersion: item.sourceVersion,
  };
}

/** "OrcaSlicer 2.3.1 · " — the engine and, for a System row, the catalog version it was ingested from. */
function engineLabel({ sourceFormat, sourceVersion }: PickerItem) {
  const engine =
    sourceFormat === 'BambuStudio'
      ? 'Bambu Studio'
      : sourceFormat === 'OrcaSlicer'
        ? 'OrcaSlicer'
        : null;
  return engine
    ? `${engine}${sourceVersion ? ` ${sourceVersion}` : ''} · `
    : '';
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
  id,
  ref,
  'aria-describedby': describedBy,
  'aria-invalid': invalid,
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
  brand,
  modelCode,
  nozzleDiameterMm,
}: ProfilePickerComboboxProps) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  const debouncedSearch = useDebouncedValue(search, 250);
  const q = debouncedSearch.trim() || undefined;

  const machineQuery =
    useOpenFarmApiEndpointsProfilesListMachineProfilesEndpoint(
      {
        origin: 'All',
        q,
        printerId: printerId ?? undefined,
        page: 1,
        pageSize: 50,
      },
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
        brand,
        modelCode,
        nozzleDiameterMm,
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

  const query =
    kind === 'machine'
      ? machineQuery
      : kind === 'process'
        ? processQuery
        : filamentQuery;
  const searching = isLoading || search !== debouncedSearch || query.isFetching;

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
          id={id}
          ref={ref}
          aria-label={ariaLabel}
          aria-describedby={describedBy}
          aria-invalid={invalid}
          aria-haspopup="listbox"
          onKeyDown={(event) => {
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
              event.preventDefault();
              setOpen(true);
            }
          }}
          variant="outline"
          disabled={disabled}
          className="w-full min-w-0 justify-between"
        >
          <span
            className="truncate"
            title={
              disabled ? disabledPlaceholder : (resolvedLabel ?? placeholder)
            }
          >
            {disabled ? disabledPlaceholder : (resolvedLabel ?? placeholder)}
          </span>
          <ChevronsUpDownIcon data-icon="inline-end" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        className="w-[var(--radix-popover-trigger-width)] p-0"
        align="start"
      >
        <Command shouldFilter={false}>
          <CommandInput
            autoFocus
            placeholder={searchPlaceholder}
            value={search}
            onValueChange={setSearch}
          />
          <CommandList aria-label={`${kind} profiles`} aria-busy={searching}>
            <CommandGroup>
              <CommandItem
                data-checked={!value}
                value="__none"
                onSelect={() => handleSelect(null, null)}
              >
                None
              </CommandItem>
            </CommandGroup>
            {searching ? (
              <p
                role="status"
                className="px-2 py-1.5 text-xs text-muted-foreground"
              >
                Loading…
              </p>
            ) : query.isError ? (
              <div role="alert" className="flex flex-col gap-2 p-2">
                <p>Could not load profiles.</p>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => query.refetch()}
                >
                  Retry
                </Button>
              </div>
            ) : items.length === 0 ? (
              <p
                role="status"
                className="px-2 py-1.5 text-xs text-muted-foreground"
              >
                No profiles found.
              </p>
            ) : (
              <CommandGroup>
                {items.map((item) => (
                  <CommandItem
                    data-checked={value === item.id}
                    key={item.id}
                    value={item.id}
                    onSelect={() => handleSelect(item.id, item.name)}
                  >
                    <div className="flex min-w-0 flex-col gap-1">
                      <span className="truncate" title={item.name}>
                        {item.name}
                      </span>
                      <span className="text-xs text-muted-foreground">
                        {engineLabel(item)}
                        {item.origin === 'System'
                          ? `System${item.vendorName ? `: ${item.vendorName}` : ''}`
                          : 'User'}
                      </span>
                      {kind === 'filament' && modelCode && nozzleDiameterMm && (
                        <span className="text-xs text-muted-foreground">
                          Compatible with {modelCode} · {nozzleDiameterMm} mm
                        </span>
                      )}
                    </div>
                  </CommandItem>
                ))}
              </CommandGroup>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
