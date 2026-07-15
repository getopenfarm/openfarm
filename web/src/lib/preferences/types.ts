export type SidebarVariant = 'flat' | 'grouped' | 'rail';
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
  sidebarVariant: SidebarVariant;
  density: Density;
  listViews: Partial<Record<ListPageKey, ViewMode>>;
}
