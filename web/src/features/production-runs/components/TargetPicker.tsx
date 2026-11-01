import type { Ref } from 'react';
import {
  LibraryItemPicker,
  type LibraryTarget,
} from '@/components/library-item-picker/LibraryItemPicker';
import { usePrintedTargetCatalog } from '@/features/assemblies/components/useBomCatalog';

export { TargetThumbnail } from '@/components/library-item-picker/LibraryItemPicker';
export type RunTarget = LibraryTarget;

/** Run targets must have complete print requirements; an assembly definition can include incomplete parts. */
export function TargetPicker({
  onPick,
  label,
  disabled,
  triggerRef,
  shortcuts = false,
}: {
  onPick: (target: RunTarget) => void;
  label?: string;
  disabled?: boolean;
  triggerRef?: Ref<HTMLButtonElement>;
  shortcuts?: boolean;
}) {
  const catalog = usePrintedTargetCatalog();
  return (
    <LibraryItemPicker
      label={label}
      onPick={onPick}
      disabled={disabled}
      triggerRef={triggerRef}
      shortcuts={shortcuts}
      isPending={catalog.isPending}
      isError={catalog.isError}
      onRetry={catalog.retry}
      groups={[
        {
          kind: 'Part',
          heading: 'Parts',
          items: catalog.parts.map((part) => ({
            id: part.id ?? '',
            name: part.name ?? '',
            thumbnailBase64: part.thumbnailBase64,
            disabled: part.printRequirementsStatus !== 'Complete',
            description:
              part.printRequirementsStatus === 'Complete'
                ? `${part.unitsPerPrint ?? 1} per plate`
                : 'Print requirements incomplete',
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
