import { Layers3Icon, PencilIcon, Trash2Icon } from 'lucide-react';
import { CheckerboardThumbnail } from '@/components/checkerboard-thumbnail/CheckerboardThumbnail';
import { Button } from '@/components/ui/button';
import type { Part } from '@/features/assemblies/data/mockPartsStore';
import { formatMinutes } from '@/lib/formatMinutes';
import type { Assembly, AssemblyComponent } from '../data/mockAssembliesStore';

interface ComponentRowProps {
  component: AssemblyComponent;
  parts: Part[];
  allAssemblies: Assembly[];
  onEdit: () => void;
  onRemove: () => void;
}

/** One row in the assembly form's Parts list. Renders either a part
 * component or a nested sub-assembly component (visually distinguished by a
 * stacked-layers icon + badge). Only part rows get an Edit action — the
 * sub-assembly reference in this pass isn't user-editable via the part
 * picker, so those rows only offer Remove. */
export function ComponentRow({
  component,
  parts,
  allAssemblies,
  onEdit,
  onRemove,
}: ComponentRowProps) {
  const isSubAssembly = Boolean(component.subAssemblyId);
  const part = component.partId
    ? parts.find((p) => p.id === component.partId)
    : undefined;
  const subAssembly = component.subAssemblyId
    ? allAssemblies.find((a) => a.id === component.subAssemblyId)
    : undefined;

  const name = isSubAssembly
    ? (subAssembly?.name ?? 'Unknown assembly')
    : (part?.name ?? 'Unknown part');

  const meta = isSubAssembly
    ? `${subAssembly?.components.length ?? 0} components`
    : part
      ? `${formatMinutes(part.estimatedTimeMinutes)} · ${part.weightGrams} g`
      : 'Part not found';

  return (
    <div className="flex items-center gap-3 rounded-lg border p-3">
      {isSubAssembly ? (
        <div className="flex size-10 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
          <Layers3Icon className="size-5" />
        </div>
      ) : (
        <CheckerboardThumbnail className="size-10 rounded-md" />
      )}
      <div className="flex min-w-0 flex-1 flex-col">
        <div className="flex items-center gap-1.5">
          <span className="truncate font-medium">{name}</span>
          {isSubAssembly ? (
            <span className="shrink-0 rounded-full bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
              Sub-assembly
            </span>
          ) : null}
        </div>
        <span className="text-xs text-muted-foreground">{meta}</span>
      </div>
      <span className="font-mono text-sm font-bold">×{component.quantity}</span>
      <div className="flex items-center gap-1">
        {isSubAssembly ? null : (
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            aria-label="Edit component"
            onClick={onEdit}
          >
            <PencilIcon />
          </Button>
        )}
        <Button
          type="button"
          variant="ghost"
          size="icon-sm"
          aria-label="Remove component"
          className="text-destructive hover:bg-destructive/10 hover:text-destructive"
          onClick={onRemove}
        >
          <Trash2Icon />
        </Button>
      </div>
    </div>
  );
}
