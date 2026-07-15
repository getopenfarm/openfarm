export type ViewMode = 'table' | 'grid';

export type ListPageKey =
  | 'printers'
  | 'jobs'
  | 'filament'
  | 'spools'
  | 'parts'
  | 'assemblies'
  | 'profiles'
  | 'maintenance';

export interface Preferences {
  listViews: Partial<Record<ListPageKey, ViewMode>>;
}
