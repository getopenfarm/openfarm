import { PackageIcon, PencilIcon, PlusIcon, XIcon } from 'lucide-react';
import { type Ref, useEffect, useRef, useState } from 'react';
import { EmptyState, ErrorState } from '@/components/empty-state/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Command,
  CommandEmpty,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import { Input } from '@/components/ui/input';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { Skeleton } from '@/components/ui/skeleton';
import { useAllCostItems } from '@/features/cost-catalog/api';
import {
  categoryLabel,
  formatQuantity,
  formatRate,
  unitLabels,
  usage,
} from '@/features/cost-catalog/costing';
import type {
  AssemblyCostInput,
  CostItemResponse,
} from '@/lib/api/generated/endpoints.schemas';
import { formatCost } from '@/lib/formatUnits';
import { pluralize } from '@/lib/pluralize';
import { CostLineDialog } from './CostLineDialog';

const usageId = (catalogItemId: string) => `cost-usage-${catalogItemId}`;

const rateText = (item: CostItemResponse) =>
  item.effectiveRate == null
    ? 'Not priced'
    : `${formatRate(item.effectiveRate)} / ${unitLabels[item.pricing?.unit ?? 'Piece']}`;

const byName = (a: string, b: string) => a.localeCompare(b);

export function CostItemsEditor({
  lines,
  onChange,
  structureItemIds,
}: {
  lines: AssemblyCostInput[];
  onChange: (lines: AssemblyCostInput[]) => void;
  /** Legacy component lines still in Structure; a cost item cannot be in both sections. */
  structureItemIds: string[];
}) {
  const catalog = useAllCostItems();
  const items = catalog.data ?? [];
  const itemFor = (id: string | undefined) =>
    items.find((item) => item.id === id);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [focusId, setFocusId] = useState<string | null>(null);
  const addButton = useRef<HTMLButtonElement>(null);
  const editing = lines.find((line) => line.catalogItemId === editingId);
  // Sorted for display so Save (which returns costs unordered) does not reshuffle rows.
  const rows = [...lines].sort((a, b) =>
    byName(
      itemFor(a.catalogItemId)?.name ?? '',
      itemFor(b.catalogItemId)?.name ?? '',
    ),
  );

  // Focus the new row's usage field once it has rendered.
  // biome-ignore lint/correctness/useExhaustiveDependencies: re-check whenever lines change
  useEffect(() => {
    if (!focusId) return;
    const input = document.getElementById(usageId(focusId));
    if (!input) return;
    input.focus();
    // Select the default so typing replaces it ("1" then "4" must not become "14").
    if (input instanceof HTMLInputElement) input.select();
    setFocusId(null);
  }, [focusId, lines]);

  function add(item: CostItemResponse) {
    const id = item.id ?? '';
    const unit = item.pricing?.unit ?? 'Piece';
    onChange([
      ...lines,
      {
        catalogItemId: id,
        purpose: ['Packaging', 'Shipping'].includes(
          item.pricing?.category ?? '',
        )
          ? 'FinalProduct'
          : 'Production',
        inputMode: 'Quantity',
        // Counts default to 1; measured usage must be typed, so it starts unset (0).
        quantity: unit === 'Piece' || unit === 'Application' ? 1 : 0,
        pieces: 1,
      },
    ]);
    // Area items open the dialog first: area versus cut is a real decision.
    if (unit === 'SquareMetre') setEditingId(id);
    else setFocusId(id);
  }

  function patch(id: string, change: Partial<AssemblyCostInput>) {
    onChange(
      lines.map((line) =>
        line.catalogItemId === id ? { ...line, ...change } : line,
      ),
    );
  }

  const ready = !catalog.isPending && !catalog.isError;
  return (
    <section
      aria-labelledby="cost-items-heading"
      className="rounded-lg border bg-card"
    >
      <div className="flex items-center gap-3 border-b px-4 py-3">
        <h3 id="cost-items-heading" className="font-medium">
          Cost items
        </h3>
        <span className="text-sm text-muted-foreground tabular-nums">
          {pluralize(lines.length, 'item')}
        </span>
        <CostItemPicker
          items={items}
          addedIds={lines.map((line) => line.catalogItemId ?? '')}
          structureIds={structureItemIds}
          disabled={!ready}
          triggerRef={addButton}
          onPick={add}
        />
      </div>
      {catalog.isPending ? (
        <Skeleton className="m-4 h-12 w-auto" />
      ) : catalog.isError ? (
        <ErrorState
          size="sm"
          bordered={false}
          title="Could not load cost items"
          onRetry={() => catalog.refetch()}
        />
      ) : lines.length === 0 ? (
        <EmptyState
          size="sm"
          bordered={false}
          title="No cost items"
          description="Add hardware, packaging, labor, or shipping used per finished product."
        />
      ) : (
        <ul className="divide-y">
          {rows.map((line) => (
            <CostRow
              key={line.catalogItemId}
              line={line}
              item={itemFor(line.catalogItemId)}
              onUsage={(change) => patch(line.catalogItemId ?? '', change)}
              onEdit={() => setEditingId(line.catalogItemId ?? '')}
              onRemove={() => {
                onChange(
                  lines.filter(
                    (other) => other.catalogItemId !== line.catalogItemId,
                  ),
                );
                addButton.current?.focus();
              }}
            />
          ))}
        </ul>
      )}
      <CostLineDialog
        open={!!editing}
        onOpenChange={(open) => {
          if (!open) setEditingId(null);
        }}
        line={editing ?? null}
        item={itemFor(editing?.catalogItemId)}
        onSave={(next) => {
          patch(next.catalogItemId ?? '', next);
          setEditingId(null);
        }}
      />
    </section>
  );
}

function CostRow({
  line,
  item,
  onUsage,
  onEdit,
  onRemove,
}: {
  line: AssemblyCostInput;
  item: CostItemResponse | undefined;
  onUsage: (change: Partial<AssemblyCostInput>) => void;
  onEdit: () => void;
  onRemove: () => void;
}) {
  const id = line.catalogItemId ?? '';
  const name = item?.name ?? 'Item unavailable';
  const unit = item?.pricing?.unit ?? 'Piece';
  const sized = line.inputMode === 'Dimensions';
  const consumed = usage(line, unit);
  const purpose =
    line.purpose === 'FinalProduct'
      ? 'Final packing and delivery'
      : 'Production';
  const unitText = unit === 'Hour' ? 'min' : unitLabels[unit];
  const cutUnits =
    line.lengthUnit === line.widthUnit
      ? `${formatQuantity(line.length ?? 0)} × ${formatQuantity(line.width ?? 0)} ${line.lengthUnit ?? 'cm'}`
      : `${formatQuantity(line.length ?? 0)} ${line.lengthUnit ?? 'cm'} × ${formatQuantity(line.width ?? 0)} ${line.widthUnit ?? 'cm'}`;
  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-2 px-2 py-2 sm:px-3">
      <div className="flex size-10 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
        <PackageIcon className="size-4" />
      </div>
      <div className="flex min-w-0 flex-1 basis-40 flex-col">
        <span
          title={name}
          className="line-clamp-2 text-sm font-medium break-words"
        >
          {name}
        </span>
        <span className="text-xs text-muted-foreground">
          {item
            ? `${categoryLabel(item.pricing?.category ?? 'Other')} · ${purpose}`
            : 'Not in catalog'}
        </span>
      </div>
      <div className="ml-auto flex items-center gap-3">
        {sized ? (
          <span className="text-sm tabular-nums">
            {cutUnits}
            {(line.pieces ?? 1) > 1 ? ` × ${line.pieces}` : ''}
          </span>
        ) : (
          <span className="flex items-center gap-1.5">
            <UsageInput
              id={usageId(id)}
              label={`${unit === 'Hour' ? 'Minutes' : 'Quantity'} for ${name}`}
              value={line.quantity ?? 0}
              whole={unit === 'Piece'}
              onValue={(quantity) => onUsage({ quantity })}
            />
            <span className="w-9 text-xs text-muted-foreground">
              {unitText}
            </span>
          </span>
        )}
        <span className="min-w-16 text-right text-sm tabular-nums">
          {item?.effectiveRate == null ? (
            <Badge variant="outline">Not priced</Badge>
          ) : consumed > 0 ? (
            formatCost(consumed * item.effectiveRate)
          ) : (
            '—'
          )}
        </span>
        <span className="flex items-center">
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            aria-label={`Edit ${name}`}
            onClick={onEdit}
          >
            <PencilIcon />
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            className="text-muted-foreground hover:text-destructive"
            aria-label={`Remove ${name}`}
            onClick={onRemove}
          >
            <XIcon />
          </Button>
        </span>
      </div>
    </li>
  );
}

/** Keeps its own text so typing "0.5" is not clobbered by the unset-as-0 rule. */
function UsageInput({
  id,
  label,
  value,
  whole,
  onValue,
}: {
  id: string;
  label: string;
  value: number;
  whole: boolean;
  onValue: (value: number) => void;
}) {
  const [text, setText] = useState(value ? String(value) : '');
  // Mirror changes made elsewhere (the dialog) without touching a partial entry like "0.".
  // biome-ignore lint/correctness/useExhaustiveDependencies: compare against the current text only when value changes
  useEffect(() => {
    if (Number(text) !== value) setText(value ? String(value) : '');
  }, [value]);
  return (
    <Input
      id={id}
      type="number"
      aria-label={label}
      required
      min={whole ? 1 : '0.000000001'}
      step={whole ? 1 : 'any'}
      value={text}
      className="h-8 w-20 tabular-nums"
      onChange={(event) => {
        setText(event.target.value);
        onValue(event.target.value === '' ? 0 : Number(event.target.value));
      }}
      onKeyDown={(event) => {
        // Enter confirms the field; it must not submit the whole assembly form.
        if (event.key === 'Enter') {
          event.preventDefault();
          event.currentTarget.blur();
        }
      }}
    />
  );
}

function CostItemPicker({
  items,
  addedIds,
  structureIds,
  disabled,
  triggerRef,
  onPick,
}: {
  items: CostItemResponse[];
  addedIds: string[];
  structureIds: string[];
  disabled: boolean;
  triggerRef: Ref<HTMLButtonElement>;
  onPick: (item: CostItemResponse) => void;
}) {
  const [open, setOpen] = useState(false);
  const picked = useRef(false);
  const sorted = [...items].sort((a, b) => byName(a.name ?? '', b.name ?? ''));
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          ref={triggerRef}
          type="button"
          size="sm"
          className="ml-auto"
          disabled={disabled}
        >
          <PlusIcon data-icon="inline-start" /> Add cost item
        </Button>
      </PopoverTrigger>
      <PopoverContent
        className="w-80 p-0"
        align="end"
        onCloseAutoFocus={(event) => {
          // After a pick, focus belongs to the new row (or the dialog), not the trigger.
          if (!picked.current) return;
          picked.current = false;
          event.preventDefault();
        }}
      >
        <Command>
          <CommandInput autoFocus placeholder="Search cost items" />
          <CommandList>
            <CommandEmpty>
              {items.length ? 'No matches.' : 'No cost items in the catalog.'}
            </CommandEmpty>
            {sorted.map((item) => {
              const id = item.id ?? '';
              const reason = addedIds.includes(id)
                ? 'Added'
                : structureIds.includes(id)
                  ? 'In Structure'
                  : null;
              return (
                <CommandItem
                  key={id}
                  value={`${item.name ?? ''} ${id}`}
                  keywords={[item.pricing?.category ?? '']}
                  disabled={reason != null}
                  onSelect={() => {
                    picked.current = true;
                    onPick(item);
                    setOpen(false);
                  }}
                >
                  <span className="flex min-w-0 flex-1 flex-col">
                    <span title={item.name} className="truncate">
                      {item.name}
                    </span>
                    <span className="text-xs text-muted-foreground">
                      {reason ??
                        `${categoryLabel(item.pricing?.category ?? 'Other')} · ${rateText(item)}`}
                    </span>
                  </span>
                </CommandItem>
              );
            })}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
