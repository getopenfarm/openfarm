/** "Bambu Studio 2.7.1" — the engine name as the operator knows it, then the exact version. */
export function formatSlicer(
  engine: string | null | undefined,
  version: string | null | undefined,
): string {
  const name = engine === 'BambuStudio' ? 'Bambu Studio' : (engine ?? '');
  return [name, version].filter(Boolean).join(' ');
}
