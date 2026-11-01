import {
  ChevronRightIcon,
  MinusIcon,
  NutIcon,
  PlusIcon,
  XIcon,
} from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';
import { EditorSectionHeader } from '@/components/editor-section/EditorSectionHeader';
import { EmptyState } from '@/components/empty-state/EmptyState';
import {
  LibraryItemPicker,
  TargetThumbnail,
} from '@/components/library-item-picker/LibraryItemPicker';
import { PermissionButton as Button } from '@/components/PermissionButton';
import { Card, CardContent } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
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

// Not crypto.randomUUID: browsers expose it only on HTTPS or localhost, and farms are often opened over http://<lan-ip>.
let lastNewKey = 0;

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
  if (kind !== 'NonPrintedComponent')
    return (
      <TargetThumbnail
        kind={kind}
        thumbnailBase64={thumbnail}
        className={className}
      />
    );
  return (
    <div
      className={cn(
        'flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-md bg-muted text-muted-foreground',
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
        <NutIcon className="size-4" />
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
        key: `new-${++lastNewKey}`,
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
    <Card role="region" aria-labelledby="bom-structure-heading">
      <EditorSectionHeader
        title="Structure"
        titleId="bom-structure-heading"
        count={lines.length}
        description="Choose parts and sub-assemblies to define this assembly."
        action={<AddLinePicker catalog={catalog} onPick={handlePick} />}
      />
      <CardContent className="px-0">
        {lines.length === 0 ? (
          <EmptyState
            size="sm"
            bordered={false}
            title="No items yet"
            description="Add the parts and sub-assemblies this assembly needs."
          />
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
                        min={
                          line.targetKind === 'NonPrintedComponent' &&
                          (catalog.components.find(
                            (c) => c.id === line.targetId,
                          )?.pricing?.unit ?? 'Piece') !== 'Piece'
                            ? '0.000000001'
                            : 1
                        }
                        step={
                          line.targetKind === 'NonPrintedComponent' &&
                          (catalog.components.find(
                            (c) => c.id === line.targetId,
                          )?.pricing?.unit ?? 'Piece') !== 'Piece'
                            ? 'any'
                            : 1
                        }
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
      </CardContent>
    </Card>
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

function AddLinePicker({
  catalog,
  onPick,
}: {
  catalog: BomCatalog;
  onPick: (kind: BomLineTargetKind, id: string, name: string) => void;
}) {
  return (
    <LibraryItemPicker
      label="Add part or assembly"
      permission="assemblies:create|assemblies:edit"
      shortcuts
      isPending={catalog.targets.isPending}
      isError={catalog.targets.isError}
      onRetry={catalog.targets.retry}
      onPick={(target) => onPick(target.kind, target.id, target.name)}
      groups={[
        {
          kind: 'Part',
          heading: 'Parts',
          items: catalog.parts.map((part) => ({
            id: part.id ?? '',
            name: part.name ?? '',
            thumbnailBase64: part.thumbnailBase64,
          })),
        },
        {
          kind: 'Assembly',
          heading: 'Assemblies',
          items: catalog.assemblies.map((assembly) => ({
            id: assembly.id ?? '',
            name: assembly.name ?? '',
          })),
        },
      ]}
    />
  );
}
