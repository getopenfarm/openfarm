import type { FilamentResponse } from '@/lib/api/generated/endpoints.schemas';

/** The add-spool page for one filament. A filament with one color has it preselected: the form reads its defaults only once. */
export function addSpoolUrl(
  filament: Pick<FilamentResponse, 'id' | 'variants'>,
  variantId?: string,
) {
  const params = new URLSearchParams({ filamentId: filament.id ?? '' });
  const color =
    variantId ??
    (filament.variants?.length === 1 ? filament.variants[0].id : undefined);
  if (color) params.set('variantId', color);
  return `/filament/spools/new?${params}`;
}
