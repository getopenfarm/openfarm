import {
  BoxIcon,
  ClipboardListIcon,
  ComponentIcon,
  HistoryIcon,
  LayersIcon,
  LayoutDashboardIcon,
  type LucideIcon,
  Package2Icon,
  PrinterIcon,
  SlidersHorizontalIcon,
  WrenchIcon,
} from 'lucide-react';

export interface NavItem {
  title: string;
  url: string;
  icon: LucideIcon;
}

export interface NavSection {
  label: string;
  items: NavItem[];
}

const dashboard: NavItem = {
  title: 'Dashboard',
  url: '/',
  icon: LayoutDashboardIcon,
};
const printers: NavItem = {
  title: 'Printers',
  url: '/printers',
  icon: PrinterIcon,
};
const jobs: NavItem = { title: 'Jobs', url: '/jobs', icon: ClipboardListIcon };
const filament: NavItem = {
  title: 'Filament',
  url: '/filament',
  icon: LayersIcon,
};
const spools: NavItem = {
  title: 'Spools',
  url: '/spools',
  icon: Package2Icon,
};
const parts: NavItem = { title: 'Parts', url: '/parts', icon: BoxIcon };
const assemblies: NavItem = {
  title: 'Assemblies',
  url: '/assemblies',
  icon: ComponentIcon,
};
const profiles: NavItem = {
  title: 'Profiles',
  url: '/profiles',
  icon: SlidersHorizontalIcon,
};
const maintenance: NavItem = {
  title: 'Maintenance',
  url: '/maintenance',
  icon: WrenchIcon,
};
const activityLog: NavItem = {
  title: 'Activity Log',
  url: '/activity-log',
  icon: HistoryIcon,
};

export const navSections: NavSection[] = [
  { label: 'Operate', items: [dashboard, printers, jobs] },
  { label: 'Materials', items: [filament, spools] },
  { label: 'Library', items: [parts, assemblies, profiles, maintenance] },
];

export const bottomNavItems: NavItem[] = [activityLog];
