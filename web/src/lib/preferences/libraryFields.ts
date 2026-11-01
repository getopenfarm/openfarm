import type { FieldVisibility, LibraryPageKey, ViewMode } from './types';

const partFields = [
  'fileType',
  'material',
  'nozzleDiameter',
  'estimate',
  'tags',
];
const assemblyFields = ['description', 'lineCount'];

export function libraryFieldIds(page: LibraryPageKey, view: ViewMode) {
  return page === 'assemblies'
    ? assemblyFields
    : partFields.filter((id) => view === 'table' || id !== 'tags');
}

/** Resolve supported boolean overrides only; fixed columns never become hideable. */
export function resolveLibraryFields(
  page: LibraryPageKey,
  view: ViewMode,
  saved: unknown,
): FieldVisibility {
  const overrides = saved && typeof saved === 'object' ? saved : {};
  return Object.fromEntries(
    libraryFieldIds(page, view).map((id) => {
      const value = Object.hasOwn(overrides, id)
        ? (overrides as Record<string, unknown>)[id]
        : undefined;
      return [
        id,
        typeof value === 'boolean'
          ? value
          : view === 'table' ||
            (page === 'parts' && ['fileType', 'material'].includes(id)),
      ];
    }),
  );
}
