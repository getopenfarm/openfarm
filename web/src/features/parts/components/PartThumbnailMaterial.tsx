import { Badge } from '@/components/ui/badge';
import type { PartSummaryResponse } from '@/lib/api/generated/endpoints.schemas';

export function PartThumbnailMaterial({
  part,
  disabled,
}: {
  part: PartSummaryResponse;
  disabled: boolean;
}) {
  const materials = [...new Set(part.materialTypes ?? [])];
  const fallback = part.requiresSlicing ? 'Any material' : 'Unknown material';
  const fullLabel = materials.length ? materials.join(', ') : fallback;
  return (
    <Badge asChild variant="secondary" className="max-w-full">
      <button
        type="button"
        disabled={disabled}
        aria-label={`Material: ${fullLabel}`}
        onClick={(event) => event.stopPropagation()}
      >
        <span className="truncate">{materials[0] ?? fallback}</span>
        {materials.length > 1 && <span>+{materials.length - 1}</span>}
      </button>
    </Badge>
  );
}
