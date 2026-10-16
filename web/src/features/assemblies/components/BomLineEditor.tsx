import {
  BoxesIcon,
  ChevronRightIcon,
  ImageOffIcon,
  MinusIcon,
  NutIcon,
  PlusIcon,
  XIcon,
} from 'lucide-react';
import { useEffect, useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Command,
  CommandEmpty,
  CommandGroup,
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
import { useOpenFarmApiEndpointsAssembliesGetAssemblyEndpoint } from '@/lib/api/generated/assemblies/assemblies';
import { cn } from '@/lib/utils';
import { type BomCatalog, useBomCatalog } from './useBomCatalog';

export type BomLineTargetKind = 'Part' | 'Assembly' | 'NonPrintedComponent';

export interface EditableLine {
  /** Stable client-side key — the server line id once persisted, otherwise a generated temp id. */
  key: string;
  /** Set once this line has been persisted (edit mode) — its target is immutable, quantity-only edits allowed. */
  lineId?: string;
  targetKind: BomLineTargetKind;
  targetId: string;
  targetName: string;
  quantity: number;
}

const KIND_LABELS: Record<BomLineTargetKind, string> = {
  Part: 'Part',
  Assembly: 'Assembly',
  NonPrintedComponent: 'Component',
};

function LineThumb({
  kind,
  thumbnail,
  className,
}: {
  kind: BomLineTargetKind;
  thumbnail?: string | null;
  className?: string;
}) {
  const Icon =
    kind === 'Assembly'
      ? BoxesIcon
      : kind === 'NonPrintedComponent'
        ? NutIcon
        : ImageOffIcon;
  return (
    <div
      className={cn(
        'flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-md bg-muted text-muted-foreground',
        kind === 'Assembly' && 'bg-primary/10 text-primary',
        className,
      )}
    >
      {thumbnail ? (
        <img
          src={`data:image/png;base64,${thumbnail}`}
          alt=""
          className="size-full object-contain"
        />
      ) : (
        <Icon className="size-4" />
      )}
    </div>
  );
}

interface BomLineEditorProps {
  lines: EditableLine[];
  onChange: (lines: EditableLine[]) => void;
  /** Excludes this assembly from the picker (edit mode — a self-reference is always rejected server-side, but hiding it is better UX). */
  excludeAssemblyId?: string;
}

export function BomLineEditor({
  lines,
  onChange,
  excludeAssemblyId,
}: BomLineEditorProps) {
  const catalog = useBomCatalog(excludeAssemblyId);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  function handlePick(kind: BomLineTargetKind, id: string, name: string) {
    const existing = lines.find(
      (l) => l.targetKind === kind && l.targetId === id,
    );
    if (existing) {
      const quantity = existing.quantity + 1;
      onChange(
        lines.map((l) => (l.key === existing.key ? { ...l, quantity } : l)),
      );
      toast(`${name} already listed. Qty now ${quantity}.`);
      return;
    }
    onChange([
      ...lines,
      {
        key: `new-${crypto.randomUUID()}`,
        targetKind: kind,
        targetId: id,
        targetName: name,
        quantity: 1,
      },
    ]);
  }

  function setQuantity(key: string, quantity: number) {
    onChange(lines.map((l) => (l.key === key ? { ...l, quantity } : l)));
  }

  function toggle(key: string) {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (!next.delete(key)) next.add(key);
      return next;
    });
  }

  return (
    <section
      aria-labelledby="bom-structure-heading"
      className="rounded-lg border bg-card"
    >
      <div className="flex items-center gap-3 border-b px-4 py-3">
        <h3 id="bom-structure-heading" className="font-medium">
          Structure
        </h3>
        <span className="text-sm text-muted-foreground tabular-nums">
          {lines.length} {lines.length === 1 ? 'line' : 'lines'}
        </span>
        <AddLinePicker catalog={catalog} onPick={handlePick} />
      </div>

      {lines.length === 0 ? (
        <div className="flex flex-col items-center gap-1 px-4 py-12 text-center">
          <p className="font-medium">No lines</p>
          <p className="text-sm text-muted-foreground">
            Add parts, sub-assemblies, or components. Press{' '}
            <kbd className="rounded border bg-muted px-1 text-xs">/</kbd> to
            search.
          </p>
        </div>
      ) : (
        <ul className="divide-y">
          {lines.map((line) => {
            const isAssembly = line.targetKind === 'Assembly';
            const isOpen = expanded.has(line.key);
            const name = line.targetName || line.targetId;
            return (
              <li key={line.key}>
                <div className="flex items-center gap-3 px-2 py-2 sm:px-3">
                  {isAssembly ? (
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      aria-expanded={isOpen}
                      aria-label={`${isOpen ? 'Collapse' : 'Expand'} ${name}`}
                      onClick={() => toggle(line.key)}
                    >
                      <ChevronRightIcon
                        className={cn(
                          'transition-transform motion-reduce:transition-none',
                          isOpen && 'rotate-90',
                        )}
                      />
                    </Button>
                  ) : (
                    <span className="size-7 shrink-0" />
                  )}
                  <LineThumb
                    kind={line.targetKind}
                    thumbnail={catalog.thumbnailFor(line.targetId)}
                  />
                  <div className="flex min-w-0 flex-1 flex-col">
                    <span
                      title={name}
                      className="line-clamp-2 text-sm font-medium break-words"
                    >
                      {name}
                    </span>
                    <span className="text-xs text-muted-foreground">
                      {KIND_LABELS[line.targetKind]}
                    </span>
                  </div>
                  <div className="flex items-center">
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`Decrease ${name}`}
                      disabled={line.quantity <= 1}
                      onClick={() => setQuantity(line.key, line.quantity - 1)}
                    >
                      <MinusIcon />
                    </Button>
                    <Input
                      type="number"
                      min={1}
                      step={1}
                      value={line.quantity}
                      onChange={(e) =>
                        setQuantity(line.key, Number(e.target.value))
                      }
                      className="h-8 w-16 text-center tabular-nums [appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none"
                      aria-label={`Quantity for ${name}`}
                    />
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`Increase ${name}`}
                      onClick={() => setQuantity(line.key, line.quantity + 1)}
                    >
                      <PlusIcon />
                    </Button>
                  </div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-sm"
                    className="text-muted-foreground hover:text-destructive"
                    onClick={() =>
                      onChange(lines.filter((l) => l.key !== line.key))
                    }
                    aria-label={`Remove ${name}`}
                  >
                    <XIcon />
                  </Button>
                </div>
                {isAssembly && isOpen && (
                  <SubAssemblyLines
                    assemblyId={line.targetId}
                    catalog={catalog}
                  />
                )}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

/** Read-only children of a sub-assembly, indented on a connector rail. Recurses — the server rejects cycles. */
function SubAssemblyLines({
  assemblyId,
  catalog,
}: {
  assemblyId: string;
  catalog: BomCatalog;
}) {
  const query =
    useOpenFarmApiEndpointsAssembliesGetAssemblyEndpoint(assemblyId);
  const children = query.data?.data?.lines ?? [];
  const [expanded, setExpanded] = useState<Set<string>>(new Set());

  return (
    <ul className="mb-2 ml-[1.625rem] border-l-2 border-primary/20 pl-3 sm:ml-[1.875rem]">
      {query.isLoading ? (
        <li className="py-2">
          <Skeleton className="h-8 w-48" />
        </li>
      ) : (
        children.map((child) => {
          const id = child.id ?? '';
          const kind = (child.targetKind ?? 'Part') as BomLineTargetKind;
          const isOpen = expanded.has(id);
          return (
            <li key={id}>
              <div className="flex items-center gap-2 py-1.5">
                {kind === 'Assembly' ? (
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-xs"
                    aria-expanded={isOpen}
                    aria-label={`${isOpen ? 'Collapse' : 'Expand'} ${child.targetName}`}
                    onClick={() =>
                      setExpanded((prev) => {
                        const next = new Set(prev);
                        if (!next.delete(id)) next.add(id);
                        return next;
                      })
                    }
                  >
                    <ChevronRightIcon
                      className={cn(
                        'transition-transform motion-reduce:transition-none',
                        isOpen && 'rotate-90',
                      )}
                    />
                  </Button>
                ) : (
                  <span className="size-6 shrink-0" />
                )}
                <LineThumb
                  kind={kind}
                  thumbnail={catalog.thumbnailFor(child.targetId ?? '')}
                  className="size-8"
                />
                <span
                  title={child.targetName ?? undefined}
                  className="line-clamp-2 min-w-0 flex-1 text-sm break-words"
                >
                  {child.targetName}
                </span>
                <span className="pr-3 text-sm text-muted-foreground tabular-nums">
                  ×{child.quantity}
                </span>
              </div>
              {kind === 'Assembly' && isOpen && child.targetId && (
                <SubAssemblyLines
                  assemblyId={child.targetId}
                  catalog={catalog}
                />
              )}
            </li>
          );
        })
      )}
    </ul>
  );
}

function isTypingTarget(target: EventTarget | null) {
  return (
    target instanceof HTMLElement &&
    (target.isContentEditable ||
      ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))
  );
}

function AddLinePicker({
  catalog,
  onPick,
}: {
  catalog: BomCatalog;
  onPick: (kind: BomLineTargetKind, id: string, name: string) => void;
}) {
  const [open, setOpen] = useState(false);

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      const modK = e.key === 'k' && (e.metaKey || e.ctrlKey);
      const slash = e.key === '/' && !isTypingTarget(e.target);
      if (modK || slash) {
        e.preventDefault();
        setOpen(true);
      }
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);

  const groups: {
    kind: BomLineTargetKind;
    heading: string;
    items: {
      id: string;
      name: string;
      thumbnail?: string | null;
    }[];
  }[] = [
    {
      kind: 'Part',
      heading: 'Parts',
      items: catalog.parts.map((p) => ({
        id: p.id ?? '',
        name: p.name ?? '',
        thumbnail: p.thumbnailBase64,
      })),
    },
    {
      kind: 'Assembly',
      heading: 'Assemblies',
      items: catalog.assemblies.map((a) => ({
        id: a.id ?? '',
        name: a.name ?? '',
      })),
    },
    {
      kind: 'NonPrintedComponent',
      heading: 'Components',
      items: catalog.components.map((c) => ({
        id: c.id ?? '',
        name: c.name ?? '',
      })),
    },
  ];

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button type="button" size="sm" className="ml-auto">
          <PlusIcon data-icon="inline-start" /> Add line
          <kbd className="ml-1 hidden rounded bg-primary-foreground/20 px-1 text-xs sm:inline">
            /
          </kbd>
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-80 p-0" align="end">
        <Command>
          <CommandInput
            autoFocus
            placeholder="Search parts, assemblies, components"
          />
          <CommandList>
            <CommandEmpty>No matches.</CommandEmpty>
            {groups.map(
              (group) =>
                group.items.length > 0 && (
                  <CommandGroup key={group.kind} heading={group.heading}>
                    {group.items.map((item) => (
                      <CommandItem
                        key={item.id}
                        value={`${group.kind}:${item.id}`}
                        keywords={[item.name]}
                        onSelect={() => {
                          onPick(group.kind, item.id, item.name);
                          setOpen(false);
                        }}
                      >
                        <LineThumb
                          kind={group.kind}
                          thumbnail={item.thumbnail}
                          className="size-7"
                        />
                        <span
                          title={item.name}
                          className="line-clamp-2 flex-1 break-words"
                        >
                          {item.name}
                        </span>
                      </CommandItem>
                    ))}
                  </CommandGroup>
                ),
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
