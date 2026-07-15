export type Density = 'comfortable' | 'compact';
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
  density: Density;
  listViews: Partial<Record<ListPageKey, ViewMode>>;
}
