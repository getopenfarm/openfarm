export type ViewMode = 'table' | 'grid';

export type DensityMode = 'small' | 'medium' | 'large';

export type ListPageKey =
  | 'printers'
  | 'jobs'
  | 'filament'
  | 'spools'
  | 'parts'
  | 'assemblies'
  | 'profiles'
  | 'maintenance';

export type LibraryPageKey = 'parts' | 'assemblies';
export type FieldVisibility = Record<string, boolean>;
export type LibraryFieldPreferences = Partial<
  Record<LibraryPageKey, Partial<Record<ViewMode, FieldVisibility>>>
>;

export interface Preferences {
  libraryFields: LibraryFieldPreferences;
  listViews: Partial<Record<ListPageKey, ViewMode>>;
  listDensities: Partial<Record<ListPageKey, DensityMode>>;
}
